using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using RCS.Licensing.Example.Provider.EFCore;

namespace RCS.Licensing.Example.Provider;

public sealed class CustomerComparer : IEqualityComparer<Customer>
{
	public bool Equals(Customer? x, Customer? y) => x?.Id == y?.Id;

	public int GetHashCode([DisallowNull] Customer obj) => obj.Id.GetHashCode();
}