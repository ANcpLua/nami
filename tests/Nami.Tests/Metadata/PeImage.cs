using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using JetBrains.Annotations;

namespace Nami.Tests.Metadata;

/// <summary>Inspects managed PE bytes without loading or executing the assembly.</summary>
[PublicAPI]
public sealed class PeImage : IDisposable
{
    private readonly PEReader _reader;

    public PeImage(ImmutableArray<byte> image)
    {
        _reader = new PEReader(image);
    }

    public PeImage(Stream stream)
    {
        _reader = new PEReader(stream, PEStreamOptions.LeaveOpen);
    }

    public void Dispose()
    {
        _reader.Dispose();
    }

    public ImmutableArray<AssemblyMetadataEntry> ReadAssemblyMetadata()
    {
        var metadata = AssemblyMetadata();
        var entries = ImmutableArray.CreateBuilder<AssemblyMetadataEntry>();
        foreach (var handle in metadata.GetAssemblyDefinition().GetCustomAttributes())
        {
            var attribute = metadata.GetCustomAttribute(handle);
            if (!Matches(metadata, attribute, "System.Reflection", "AssemblyMetadataAttribute")) continue;
            var blob = Arguments(metadata, attribute);
            var key = blob.ReadSerializedString() ??
                      throw new BadImageFormatException("Assembly metadata has a null key.");
            entries.Add(new AssemblyMetadataEntry(key, blob.ReadSerializedString()));
        }

        return entries.ToImmutable();
    }

    public string? ReadTargetFrameworkName()
    {
        var metadata = AssemblyMetadata();
        return metadata.GetAssemblyDefinition().GetCustomAttributes()
            .Select(metadata.GetCustomAttribute)
            .Where(attribute => Matches(metadata, attribute, "System.Runtime.Versioning", "TargetFrameworkAttribute"))
            .Select(attribute => Arguments(metadata, attribute).ReadSerializedString())
            .FirstOrDefault();
    }

    public ImmutableArray<AssemblyReferenceIdentity> ReadAssemblyReferences()
    {
        var metadata = AssemblyMetadata();
        return
        [
            .. metadata.AssemblyReferences.Select(metadata.GetAssemblyReference)
                .Select(reference =>
                    new AssemblyReferenceIdentity(metadata.GetString(reference.Name), reference.Version))
        ];
    }

    public bool HasCodeViewDebugEntry()
    {
        return _reader.ReadDebugDirectory().Any(entry => entry.Type == DebugDirectoryEntryType.CodeView);
    }

    private MetadataReader AssemblyMetadata()
    {
        if (!_reader.HasMetadata) throw new BadImageFormatException("The image has no managed metadata.");
        var metadata = _reader.GetMetadataReader();
        return metadata.IsAssembly
            ? metadata
            : throw new BadImageFormatException("The image is not a managed assembly.");
    }

    private static BlobReader Arguments(MetadataReader metadata, CustomAttribute attribute)
    {
        var blob = metadata.GetBlobReader(attribute.Value);
        return blob.ReadUInt16() == 1 ? blob : throw new BadImageFormatException("Invalid custom attribute prolog.");
    }

    private static bool Matches(MetadataReader metadata, CustomAttribute attribute, string typeNamespace,
        string typeName)
    {
        StringHandle name;
        StringHandle ns;
        switch (attribute.Constructor)
        {
            case { Kind: HandleKind.MemberReference } memberConstructor:
            {
                var member = metadata.GetMemberReference((MemberReferenceHandle)memberConstructor);
                if (member.Parent.Kind != HandleKind.TypeReference) return false;
                var type = metadata.GetTypeReference((TypeReferenceHandle)member.Parent);
                name = type.Name;
                ns = type.Namespace;
                break;
            }
            case { Kind: HandleKind.MethodDefinition } definitionConstructor:
            {
                var method = metadata.GetMethodDefinition((MethodDefinitionHandle)definitionConstructor);
                var type = metadata.GetTypeDefinition(method.GetDeclaringType());
                name = type.Name;
                ns = type.Namespace;
                break;
            }
            default: return false;
        }

        return metadata.StringComparer.Equals(name, typeName) && metadata.StringComparer.Equals(ns, typeNamespace);
    }
}

[PublicAPI]
public sealed record AssemblyMetadataEntry(string Key, string? Value);

[PublicAPI]
public sealed record AssemblyReferenceIdentity(string Name, Version Version);
