#if !NET11_0_OR_GREATER
#pragma warning disable IDE0130
namespace System.Runtime.CompilerServices;

#pragma warning restore IDE0130

// What the C# 15 compiler marks a union with; defined here so the same shape can be tested on net10.0.
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
internal sealed class UnionAttribute : Attribute;
#endif
