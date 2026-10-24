using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

Console.WriteLine($"Microsoft.CodeAnalysis {typeof(Compilation).Assembly.GetName().Version}; LanguageVersion names with 15: {string.Join(", ", Enum.GetNames<LanguageVersion>().Where(n => n.Contains("15")))}");

var src = """
    public union FourEyesApproval(NotRequired, PendingApproval, PartlyApproved);
    public record class NotRequired;
    public record class PendingApproval;
    public record class PartlyApproved(System.Guid Approver);
    public union IntOrString(int, string);
    public record class Plain;
    [System.Runtime.CompilerServices.Union]
    public struct Shape : System.Runtime.CompilerServices.IUnion
    {
        private readonly object? _value;
        public Shape(Circle value) { _value = value; }
        public Shape(Square value) { _value = value; }
        public object? Value => _value;
    }
    public record class Circle(double R);
    public record class Square(double S);
    """;

var tree = CSharpSyntaxTree.ParseText(src, new CSharpParseOptions(LanguageVersion.Preview));
var refs = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Select(p => MetadataReference.CreateFromFile(p));
var comp = CSharpCompilation.Create("P", [tree], refs, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
var errors = comp.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
Console.WriteLine($"compile errors: {errors.Count}{(errors.Count > 0 ? " — " + string.Join("; ", errors.Take(3)) : "")}");

foreach (var name in new[] { "FourEyesApproval", "IntOrString", "Shape", "Plain" })
{
    var t = comp.GetTypeByMetadataName(name)!;
    var cases = t.UnionCaseTypes;
    Console.WriteLine($"{name}: TypeKind={t.TypeKind}, UnionCaseTypes.IsDefault={cases.IsDefault}, [{(cases.IsDefault ? "" : string.Join(", ", cases.Select(c => c.ToDisplayString())))}]");
}
