using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RCS.Licensing.Provider.Shared;
using RCS.Licensing.Provider.Shared.Entities;

namespace RCS.Licensing.Example.Provider.MSTests;

[TestClass]
public class ExampleProviderTests : TestBase
{
	const string GuestId = "10000335";
	const string TestUserName = "gfkeogh@gmail.com";
	const string TestUserPass = "qwe_123";
	const string GuestPass = "guest";
	const string Client1CustName = "client1rcs";
	const string Client1DemoName = "demo";
	readonly static JsonSerializerOptions JOpts = new() { WriteIndented = true };

	[TestMethod]
	public async Task T100_Authenticate()
	{
		var prov = MakeProvider();
		LicenceFull? licfull = await prov.AuthenticateName(TestUserName, TestUserPass);
		Info($"LoginName -> {licfull.Id} | {licfull.Name}");
		foreach (var cust in licfull.Customers)
		{
			Info($"|  CUST {cust.Id} | {cust.Name} | {cust.DisplayName}");
			foreach (var job in cust.Jobs)
			{
				string? vtrs = job.VartreeNames == null ? null : string.Format("[{0}]", string.Join(',', job.VartreeNames));
				string? reals = job.RealCloudVartreeNames == null ? null : string.Format("[{0}]", string.Join(',', job.RealCloudVartreeNames));
				Info($"|  |  JOB {job.Id} | {job.Name} | {job.DisplayName} • {vtrs} • {reals}");
			}
		}
	}

	[TestMethod]
	public async Task T120_UpdateAccount()
	{
		var prov = MakeProvider();
		LicenceFull? licfull = await prov.AuthenticateName(TestUserName, TestUserPass);
		Info($"LoginName -> {licfull.Id} | {licfull.Name}");
		string comment = $"Updated on {DateTime.Now}";
		int ucount = await prov.UpdateAccount(licfull.Id, "GregKeogh", comment, "greg@orthogonal.com.au");
		Info($"Update count -> {ucount}");
		var user = await prov.ReadUser(licfull.Id);
		Assert.IsNotNull(user);
		Assert.AreEqual("GregKeogh", user.Name);
		Assert.AreEqual(comment, user.Comment);
		Assert.AreEqual("greg@orthogonal.com.au", user.Email);
	}

	[TestMethod]
	public async Task T200_Realms()
	{
		var prov = MakeProvider();

		var realms = await prov.ListRealms();
		Info($"Realm count -> {realms.Length}");
		foreach (var realm in realms)
		{
			Info($"|  {realm.Id} {realm.Name}");
			var users = await prov.ListUsers(realm.Id);
			foreach (var user in users)
			{
				Info($"|  |  USER {user.Id} {user.Name}");
			}
			var custs = await prov.ListCustomers(realm.Id);
			foreach (var cust in custs)
			{
				Info($"|  |  CUSTOMER {cust.Id} {cust.Name}");
			}
		}
	}

	[TestMethod]
	public async Task T300_ListCustomers()
	{
		var prov = MakeProvider();
		var custs = await prov.ListCustomers();
		Info($"Customer count -> {custs.Length}");
		foreach (var cust in custs)
		{
			Info($"| {cust.Id} {cust.Name}");
		}
	}

	[TestMethod]
	public async Task T400_ListJobs()
	{
		var prov = MakeProvider();
		var jobs = await prov.ListJobs();
		Info($"Job count -> {jobs.Length}");
		foreach (var job in jobs)
		{
			Info($"| {job.Id} {job.Name}");
		}
	}

	[TestMethod]
	public async Task T500_ListUsers()
	{
		var prov = MakeProvider();
		var users = await prov.ListUsers();
		Info($"User count -> {users.Length}");
		foreach (var user in users)
		{
			Info($"| {user.Id} {user.Uid} {user.Name}");
		}
	}

	[TestMethod]
	public async Task T700_Connect_Story()
	{
		var prov = MakeProvider();
		const string Realm1Name = "TempTestRealm1";
		const string User1Name = "temp1@testing.com.au";
		const string User2Name = "temp2@testing.com.au";

		async Task<string> EnsureRealm(string realmName)
		{
			var realms = await prov.ReadRealmsByName(realmName);
			Assert.IsTrue(realms.Length <= 1);
			if (realms.Length == 0)
			{
				var realm = new Realm() { Name = realmName };
				var result = await prov.UpsertRealm(realm);
				var realm2 = result.Entity;
				Assert.IsNotNull(realm2);
				Info($"Created realm {realm2?.Id} {realm2?.Name}");
				return realm2!.Id;
			}
			else
			{
				Info($"Realm {realms[0].Id} {realms[0].Name} already exists");
				return realms[0].Id;
			}
		}
		string rid1 = await EnsureRealm(Realm1Name);

		async Task<string> EnsureUser(string userName)
		{
			var users = await prov.ReadUsersByName(userName);
			Assert.IsTrue(users.Length <= 1);
			if (users.Length == 0)
			{
				var user = new User() { Name = userName, RoleSet = ["Analyst", "Silver"], Comment = "TESTING ONLY", Psw = "test123" };
				var result = await prov.UpsertUser(user);
				var user2 = result.Entity;
				Assert.IsNotNull(user2);
				Info($"Created user {user2.Id} {user2.Name}");
				return user2.Id;
			}
			else
			{
				Info($"User {users[0].Id} {users[0].Name} already exists");
				return users[0].Id;
			}
		}
		string uid1 = await EnsureUser(User1Name);
		string uid2 = await EnsureUser(User2Name);

		await prov.ConnectUserChildRealms(uid1, [rid1]);
		//await prov.ReplaceUserChildRealms(uid1, new string[] { rid1 });
		//await prov.ConnectRealmChildUsers(rid1, new string[] { uid1 });
	}
}