#if BAD_CLOSED_METADATA
namespace System.Runtime.CompilerServices
{
    public sealed class IsClosedTypeAttribute : System.Attribute
    {
        public int DerivedTypes { get; set; }
    }
}

namespace UnionDiagnostics
{
    public closed class ClosedMetadataBase { }
    public sealed class ClosedMetadataChild : ClosedMetadataBase { }
}
#endif
