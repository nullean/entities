// netstandard2.0 has no System.Runtime.CompilerServices.IsExternalInit, which the compiler needs
// to allow `init` accessors — and MemberInfo below is a record, which uses one. The type only has
// to exist for the compiler to find; it is never actually used at runtime.

#pragma warning disable IDE0130 // namespace intentionally doesn't match the folder — it's a polyfill for a BCL type
namespace System.Runtime.CompilerServices;

#pragma warning restore IDE0130

internal static class IsExternalInit
{
}
