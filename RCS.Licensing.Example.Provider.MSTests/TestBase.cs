using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace RCS.Licensing.Example.Provider.MSTests;

public class TestBase
{
	protected readonly IConfiguration Config;

	protected TestBase()
	{
		Config = new ConfigurationBuilder()
			.AddJsonFile("appsettings.json")
			.AddUserSecrets("9c9dd5cd-7323-46c4-aca6-b9289c171e54")
			.Build();
	}

	protected ExampleLicensingProvider MakeProvider()
	{
		string? connect = Config["CarbonApi:AdoConnect"];   // Not used at the moment
		Assert.IsNotNull(connect, "An ADO connection string to the SQL Server database must be defined in configuration. Use the settings file, user secrets (in development) or another configuration source to provide the value.");
		Info(connect);
		var prov = new ExampleLicensingProvider(connect);
		Info(prov.Description);
		return prov;
	}

	protected void Sep1(string title)
	{
		int len = title.Length + 4;
		Info("┌" + new string('─', len) + "┐");
		Info("│  " + title + "  │");
		Info("└" + new string('─', len) + "┘");
	}

	protected void Info(string message) => System.Diagnostics.Trace.WriteLine(message);

	protected void PrintJson(object value)
	{
		string json = JsonSerializer.Serialize(value, new JsonSerializerOptions() { WriteIndented = true });
		Info(json);
	}
}
