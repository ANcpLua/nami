using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ANcpLua.Roslyn.Utilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Nami.Collector.Storage.Generators;

[Generator]
public sealed class DuckDbInsertGenerator : IIncrementalGenerator
{
    private const string DuckDbTableAttribute = "Nami.Collector.Storage.DuckDbTableAttribute";
    private const string DuckDbColumnAttribute = "Nami.Collector.Storage.DuckDbColumnAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(static ctx =>
        {
            ctx.AddSource("DuckDbAttributes.g.cs", SourceText.From(DuckDbAttributeSource.Source, Encoding.UTF8));
            ctx.AddSource("StorageDefaults.g.cs", SourceText.From(StorageDefaultsSource.Source, Encoding.UTF8));
        });

        var tableTypes = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                DuckDbTableAttribute,
                static (node, _) => node is TypeDeclarationSyntax,
                static (ctx, _) => ExtractTableInfo(ctx))
            .WhereNotNull();

        context.RegisterSourceOutput(tableTypes, static (spc, tableInfo) =>
        {
            var source = DuckDbEmitter.Emit(tableInfo);
            spc.AddSource(GetHintName(tableInfo), SourceText.From(source, Encoding.UTF8));
        });

        context.RegisterSourceOutput(tableTypes.Collect(), static (spc, tables) =>
        {
            var source = DuckDbSchemaEmitter.Emit(tables);
            spc.AddSource("DuckDbGeneratedSchema.g.cs", SourceText.From(source, Encoding.UTF8));
        });
    }

    private static string GetHintName(DuckDbTableInfo tableInfo) =>
        string.IsNullOrEmpty(tableInfo.Namespace) ||
        string.Equals(tableInfo.Namespace, "<global namespace>", StringComparison.Ordinal)
            ? $"{tableInfo.TypeName}.DuckDb.g.cs"
            : $"{tableInfo.Namespace}.{tableInfo.TypeName}.DuckDb.g.cs";

    private static DuckDbTableInfo? ExtractTableInfo(GeneratorAttributeSyntaxContext ctx)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol typeSymbol)
            return null;

        var typeDecl = (TypeDeclarationSyntax)ctx.TargetNode;

        if (!typeDecl.Modifiers.Any(SyntaxKind.PartialKeyword))
            return null;

        if (ctx.Attributes is not [{ } tableAttr, ..])
            return null;

        string? tableName = null;
        string? onConflict = null;
        var appenderEligible = false;
        var derived = false;
        var arrowEligible = false;
        var indexes = "";
        var uniqueIndexes = "";
        var parameterSets = "";

        if (tableAttr.ConstructorArguments.Length > 0)
            tableName = tableAttr.GetConstructorArgument<string>(0);

        foreach (var named in tableAttr.NamedArguments)
        {
            switch (named.Key)
            {
                case "OnConflict":
                    onConflict = named.Value.Value as string;
                    break;
                case "AppenderEligible":
                    appenderEligible = named.Value.Value is true;
                    break;
                case "Derived":
                    derived = named.Value.Value is true;
                    break;
                case "ArrowEligible":
                    arrowEligible = named.Value.Value is true;
                    break;
                case "Indexes":
                    indexes = named.Value.Value as string ?? "";
                    break;
                case "UniqueIndexes":
                    uniqueIndexes = named.Value.Value as string ?? "";
                    break;
                case "ParameterSets":
                    parameterSets = named.Value.Value as string ?? "";
                    break;
            }
        }

        if (tableName is not { Length: > 0 })
            return null;

        var columnAttributeType = ctx.SemanticModel.Compilation.GetTypeByMetadataName(DuckDbColumnAttribute);

        var columns = new List<DuckDbColumnInfo>();
        var ordinal = 0;

        foreach (var prop in GetPublicMappedProperties(typeSymbol))
        {
            var columnInfo = ExtractColumnInfo(prop, ordinal, columnAttributeType);
            if (columnInfo is not null)
            {
                columns.Add(columnInfo.Value);
                // Reader ordinals follow SelectColumnList, which includes server-generated columns
                // excluded from INSERT. Advancing only for insert columns aliases every subsequent
                // generated column onto the preceding ordinal.
                ordinal++;
            }
        }

        return new DuckDbTableInfo(
            typeSymbol.ContainingNamespace.ToDisplayString(),
            typeSymbol.Name,
            typeDecl is RecordDeclarationSyntax ? "record" : "class",
            tableName,
            onConflict,
            appenderEligible,
            derived,
            arrowEligible,
            columns.ToArray().ToEquatableArray(),
            ParseIndexSpecs(tableName, indexes, columns, false)
                .Concat(ParseIndexSpecs(tableName, uniqueIndexes, columns, true))
                .ToArray()
                .ToEquatableArray(),
            ParseParameterSets(tableName, parameterSets, columns)
                .ToArray()
                .ToEquatableArray());
    }

    private static IEnumerable<DuckDbParameterSetInfo> ParseParameterSets(
        string tableName,
        string parameterSets,
        IReadOnlyCollection<DuckDbColumnInfo> columns)
    {
        if (string.IsNullOrWhiteSpace(parameterSets))
            yield break;

        var columnsByProperty = columns.ToDictionary(static column => column.PropertyName, StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var setSpec in parameterSets.Split(';'))
        {
            var separator = setSpec.IndexOf(':');
            if (separator <= 0 || separator == setSpec.Length - 1)
            {
                throw new InvalidOperationException(
                    $"DuckDB parameter set on '{tableName}' must use 'Name:Property,Property' syntax.");
            }

            var name = setSpec[..separator].Trim();
            if (!SyntaxFacts.IsValidIdentifier(name))
            {
                throw new InvalidOperationException(
                    $"DuckDB parameter set name '{name}' on '{tableName}' is not a valid C# identifier.");
            }

            if (!names.Add(name))
            {
                throw new InvalidOperationException(
                    $"DuckDB parameter set '{name}' is declared more than once on '{tableName}'.");
            }

            var setColumns = setSpec[(separator + 1)..]
                .Split(',')
                .Select(static property => property.Trim())
                .Where(static property => property.Length > 0)
                .Select(property => ResolveParameterColumn(tableName, name, property, columnsByProperty))
                .ToArray();
            if (setColumns.Length is 0)
            {
                throw new InvalidOperationException(
                    $"DuckDB parameter set '{name}' on '{tableName}' has no columns.");
            }

            yield return new DuckDbParameterSetInfo(name, setColumns.ToEquatableArray());
        }
    }

    private static DuckDbColumnInfo ResolveParameterColumn(
        string tableName,
        string parameterSet,
        string propertyName,
        IReadOnlyDictionary<string, DuckDbColumnInfo> columnsByProperty)
    {
        if (columnsByProperty.TryGetValue(propertyName, out var column))
            return column;

        throw new InvalidOperationException(
            $"DuckDB parameter set '{parameterSet}' on '{tableName}' references unknown property '{propertyName}'.");
    }

    private static IEnumerable<DuckDbIndexInfo> ParseIndexSpecs(
        string tableName,
        string indexes,
        IReadOnlyCollection<DuckDbColumnInfo> columns,
        bool isUnique)
    {
        if (string.IsNullOrWhiteSpace(indexes))
            yield break;

        var columnsByProperty = columns.ToDictionary(static column => column.PropertyName, StringComparer.Ordinal);

        foreach (var indexSpec in indexes.Split(';'))
        {
            var columnNames = indexSpec
                .Split(',')
                .Select(static column => column.Trim())
                .Where(static column => column.Length > 0)
                .Select(column => ResolveIndexColumn(tableName, column, columnsByProperty))
                .ToArray();

            if (columnNames.Length is 0)
                continue;

            yield return new DuckDbIndexInfo(
                BuildIndexName(tableName, columnNames, isUnique),
                columnNames.ToEquatableArray(),
                isUnique);
        }
    }

    private static string ResolveIndexColumn(
        string tableName,
        string configuredColumn,
        IReadOnlyDictionary<string, DuckDbColumnInfo> columnsByProperty)
    {
        if (columnsByProperty.TryGetValue(configuredColumn, out var propertyColumn))
            return propertyColumn.ColumnName;

        throw new InvalidOperationException(
            $"DuckDB index on '{tableName}' references unknown property '{configuredColumn}'.");
    }

    private static string BuildIndexName(
        string tableName,
        IReadOnlyList<string> columnNames,
        bool isUnique)
    {
        var sb = new StringBuilder(isUnique ? "uidx_" : "idx_");
        AppendIdentifierToken(sb, tableName);

        foreach (var columnName in columnNames)
        {
            sb.Append('_');
            AppendIdentifierToken(sb, columnName);
        }

        return sb.ToString();
    }

    private static void AppendIdentifierToken(StringBuilder sb, string value)
    {
        foreach (var c in value)
            sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_');
    }

    private static List<IPropertySymbol> GetPublicMappedProperties(INamedTypeSymbol typeSymbol)
    {
        var properties = new List<IPropertySymbol>();
        foreach (var member in typeSymbol.GetMembers())
        {
            if (member is not IPropertySymbol prop)
                continue;

            if (prop.DeclaredAccessibility != Accessibility.Public)
                continue;

            if (prop.GetMethod is null)
                continue;

            properties.Add(prop);
        }

        properties.Sort(ComparePropertyDeclarationOrder);
        return properties;
    }

    private static int ComparePropertyDeclarationOrder(IPropertySymbol left, IPropertySymbol right)
    {
        var leftReference = left.DeclaringSyntaxReferences.FirstOrDefault();
        var rightReference = right.DeclaringSyntaxReferences.FirstOrDefault();
        if (leftReference is null || rightReference is null)
            return StringComparer.Ordinal.Compare(left.MetadataName, right.MetadataName);

        var fileComparison = StringComparer.Ordinal.Compare(
            leftReference.SyntaxTree.FilePath,
            rightReference.SyntaxTree.FilePath);
        if (fileComparison is not 0)
            return fileComparison;

        var positionComparison = leftReference.Span.Start.CompareTo(rightReference.Span.Start);
        return positionComparison is not 0
            ? positionComparison
            : StringComparer.Ordinal.Compare(left.MetadataName, right.MetadataName);
    }

    private static DuckDbColumnInfo? ExtractColumnInfo(
        IPropertySymbol prop,
        int defaultOrdinal,
        INamedTypeSymbol? columnAttributeType)
    {
        var columnName = ToSnakeCase(prop.Name);
        var excludeFromInsert = false;
        string? sqlType = null;
        string? defaultSql = null;
        var primaryKeyOrdinal = -1;

        var colAttr = FindAttribute(prop, columnAttributeType);
        if (colAttr is not null)
        {
            foreach (var named in colAttr.NamedArguments)
            {
                switch (named.Key)
                {
                    case "ExcludeFromInsert":
                        excludeFromInsert = named.Value.Value is true;
                        break;
                    case "SqlType":
                        sqlType = named.Value.Value as string;
                        break;
                    case "DefaultSql":
                        defaultSql = named.Value.Value as string;
                        break;
                    case "PrimaryKeyOrdinal":
                        if (named.Value.Value is int p)
                            primaryKeyOrdinal = p;
                        break;
                }
            }
        }

        var propType = prop.Type.ToDisplayString();
        var isNullable = prop.Type.NullableAnnotation == NullableAnnotation.Annotated ||
                         propType.EndsWithOrdinal("?");

        return new DuckDbColumnInfo(
            prop.Name,
            columnName,
            propType,
            isNullable,
            excludeFromInsert,
            defaultOrdinal,
            sqlType,
            defaultSql,
            primaryKeyOrdinal);
    }

    private static AttributeData? FindAttribute(ISymbol symbol, INamedTypeSymbol? attributeType)
    {
        if (attributeType is null)
            return null;

        foreach (var attribute in symbol.GetAttributes())
        {
            if (attribute.AttributeClass.IsEqualTo(attributeType))
                return attribute;
        }

        return null;
    }

    private static string ToSnakeCase(string name)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                if (i > 0)
                    sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}

internal readonly record struct DuckDbTableInfo(
    string Namespace,
    string TypeName,
    string TypeKind,
    string TableName,
    string? OnConflict,
    bool AppenderEligible,
    bool Derived,
    bool ArrowEligible,
    EquatableArray<DuckDbColumnInfo> Columns,
    EquatableArray<DuckDbIndexInfo> Indexes,
    EquatableArray<DuckDbParameterSetInfo> ParameterSets);

internal readonly record struct DuckDbIndexInfo(
    string Name,
    EquatableArray<string> ColumnNames,
    bool IsUnique);

internal readonly record struct DuckDbParameterSetInfo(
    string Name,
    EquatableArray<DuckDbColumnInfo> Columns);

internal readonly record struct DuckDbColumnInfo(
    string PropertyName,
    string ColumnName,
    string PropertyType,
    bool IsNullable,
    bool ExcludeFromInsert,
    int Ordinal,
    string? SqlType,
    string? DefaultSql,
    int PrimaryKeyOrdinal);
