using Microsoft.EntityFrameworkCore;

namespace RCS.Licensing.Example.Provider.EFCore;

partial class ExampleContext
{
	readonly string _adoConnect;

	public ExampleContext(string adoConnect)
	{
		_adoConnect = adoConnect;
	}

	protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
	{
		optionsBuilder.UseSqlServer(_adoConnect);
	}
}
