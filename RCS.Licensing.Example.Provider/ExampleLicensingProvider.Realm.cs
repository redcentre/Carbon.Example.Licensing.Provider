using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RCS.Licensing.Provider.Shared;
using RCS.Licensing.Provider.Shared.Entities;
using EF = RCS.Licensing.Example.Provider.EFCore;

namespace RCS.Licensing.Example.Provider;

partial class ExampleLicensingProvider
{
	public bool SupportsRealms => true;

	public async Task<Realm?> ReadRealm(string realmId)
	{
		int id = int.Parse(realmId);
		using var context = MakeContext();
		var realm = await context.Realms.AsNoTracking().Include(r => r.Users).Include(r => r.Customers).FirstOrDefaultAsync(r => r.Id == id).ConfigureAwait(false);
		return realm == null ? null : ToRealm(realm, true);
	}

	public async Task<Realm[]> ReadRealmsByName(string realmName)
	{
		using var context = MakeContext();
		return await context.Realms.AsNoTracking()
			.Include(r => r.Users)
			.Include(r => r.Customers)
			.Where(r => r.Name == realmName)
			.AsAsyncEnumerable()
			.Select(r => ToRealm(r, true)!)
			.ToArrayAsync()
			.ConfigureAwait(false);
	}

	public async Task<Realm[]> ListRealms()
	{
		using var context = MakeContext();
		var realms = await context.Realms.AsNoTracking().ToArrayAsync().ConfigureAwait(false);
		return realms.Select(r => ToRealm(r, false)!).ToArray();
	}

	public async Task<UpsertResult<Realm>> UpsertRealm(Realm realm)
	{
		using var context = MakeContext();
		EF.Realm row;
		if (realm.Id == null)
		{
			int? newid = null;
			while (newid == null)
			{
				int tryid = Random.Shared.Next(70_000_000, 80_000_000);
				if (!await context.Realms.AnyAsync(r => r.Id == tryid).ConfigureAwait(false))
				{
					newid = tryid;
				}
			}
			row = new EF.Realm
			{
				Id = newid.Value,
				Created = DateTime.UtcNow
			};
			context.Realms.Add(row);
		}
		else
		{
			EF.Realm? oldrow = await context.Realms.FirstOrDefaultAsync(r => r.Id.ToString() == realm.Id).ConfigureAwait(false);
			if (oldrow == null)
			{
				return new UpsertResult<Realm>(null, UpsertStatusCode.NotFound, $"Realm Id {realm.Id} not found for update");
			}
			row = oldrow;
		}
		row.Name = realm.Name;
		row.Policy = realm.Policy;
		row.Inactive = realm.Inactive;
		await context.SaveChangesAsync().ConfigureAwait(false);
		row = await context.Realms.AsNoTracking().Include(r => r.Users).Include(r => r.Users).FirstAsync(r => r.Id == row.Id);
		Realm? uprealm = await RereadRealm(context, row.Id).ConfigureAwait(false);
		return new UpsertResult<Realm>(uprealm, realm.Id == null ? UpsertStatusCode.Inserted : UpsertStatusCode.Updated);
	}

	public async Task<int> DeleteRealm(string realmId)
	{
		int id = int.Parse(realmId);
		using var context = MakeContext();
		EF.Realm? realm = await context.Realms
			.Include(r => r.Users)
			.Include(r => r.Customers)
			.FirstOrDefaultAsync(r => r.Id == id)
			.ConfigureAwait(false);
		if (realm == null) return 0;
		foreach (var user in realm.Users.ToArray())
		{
			realm.Users.Remove(user);
		}
		foreach (var cust in realm.Customers.ToArray())
		{
			realm.Customers.Remove(cust);
		}
		context.Realms.Remove(realm);
		return await context.SaveChangesAsync().ConfigureAwait(false);
	}

	public async Task<string[]> ValidateRealm(string realmId)
	{
		return await Task.FromResult(Array.Empty<string>());    // Not used
	}

	public async Task<Realm?> DisconnectRealmChildUser(string realmId, string userId)
	{
		Log($"D DisconnectRealmChildUser({realmId},{userId})");
		int id = int.Parse(realmId);
		using var context = MakeContext();
		EF.Realm? realm = await context.Realms.Include(r => r.Users).FirstOrDefaultAsync(r => r.Id == id).ConfigureAwait(false);
		if (realm == null) return null;
		int uid = int.Parse(userId);
		var user = realm.Users.FirstOrDefault(u => u.Id == uid);
		if (user != null)
		{
			Log($"I DisconnectRealmChildUser | Realm {realm.Id} {realm.Name} DEL User {user.Id} {user.Name}");
			realm.Users.Remove(user);
		}
		await context.SaveChangesAsync().ConfigureAwait(false);
		return await RereadRealm(context, id);
	}

	public async Task<Realm?> ConnectRealmChildUsers(string realmId, string[] userIds)
	{
		Log($"D ConnectRealmChildUsers({realmId},{Join(userIds)})");
		int id = int.Parse(realmId);
		using var context = MakeContext();
		EF.Realm? realm = await context.Realms.Include(r => r.Users).FirstOrDefaultAsync(r => r.Id == id).ConfigureAwait(false);
		if (realm == null) return null;
		int[] uids = userIds.Select(u => int.Parse(u)).ToArray();
		int[] gotuids = realm.Users.Select(u => id).ToArray();
		int[] adduids = uids.Except(gotuids).ToArray();
		EF.User[] addusers = await context.Users.Where(u => adduids.Contains(u.Id)).ToArrayAsync().ConfigureAwait(false);
		foreach (var adduser in addusers)
		{
			Log($"I ConnectRealmChildUsers | Realm {realm.Id} {realm.Name} ADD User {adduser.Id} {adduser.Name}");
			realm.Users.Add(adduser);
		}
		await context.SaveChangesAsync().ConfigureAwait(false);
		return await RereadRealm(context, id).ConfigureAwait(false);
	}

	public async Task<Realm?> ReplaceRealmChildUsers(string realmId, string[] userIds)
	{
		Log($"D ReplaceRealmChildUsers({realmId},{Join(userIds)})");
		int id = int.Parse(realmId);
		using var context = MakeContext();
		var realm = await context.Realms.Include(r => r.Users).FirstOrDefaultAsync(r => r.Id == id).ConfigureAwait(false);
		if (realm == null) return null;
		int[] uids = userIds.Select(u => int.Parse(u)).ToArray();
		var addusers = await context.Users.Where(u => uids.Contains(u.Id)).ToArrayAsync().ConfigureAwait(false);
		realm.Users.Clear();
		foreach (var adduser in addusers)
		{
			Log($"I ReplaceRealmChildUsers | Realm {realm.Id} {realm.Name} ADD User {adduser.Id} {adduser.Name}");
			realm.Users.Add(adduser);
		}
		await context.SaveChangesAsync().ConfigureAwait(false);
		return await RereadRealm(context, id).ConfigureAwait(false);
	}

	public async Task<Realm?> DisconnectRealmChildCustomer(string realmId, string customerId)
	{
		Log($"D DisconnectRealmChildCustomer({realmId},{customerId})");
		int id = int.Parse(realmId);
		using var context = MakeContext();
		var realm = await context.Realms.Include(r => r.Customers).FirstOrDefaultAsync(r => r.Id.ToString() == realmId).ConfigureAwait(false);
		if (realm == null) return null;
		int cid = int.Parse(customerId);
		var cust = realm.Customers.FirstOrDefault(c => c.Id == cid);
		if (cust != null)
		{
			Log($"I DisconnectRealmChildUser | Realm {realm.Id} {realm.Name} DEL Customer {cust.Id} {cust.Name}");
			realm.Customers.Remove(cust);
		}
		await context.SaveChangesAsync().ConfigureAwait(false);
		return await RereadRealm(context, id);
	}

	public async Task<Realm?> ConnectRealmChildCustomers(string realmId, string[] customerIds)
	{
		Log($"D ConnectRealmChildCustomers({realmId},{Join(customerIds)})");
		int id = int.Parse(realmId);
		using var context = MakeContext();
		var realm = await context.Realms.Include(r => r.Customers).FirstOrDefaultAsync(r => r.Id == id).ConfigureAwait(false);
		if (realm == null) return null;
		int[] cids = customerIds.Select(c => int.Parse(c)).ToArray();
		int[] gotcids = realm.Customers.Select(c => c.Id).ToArray();
		int[] addcids = cids.Except(gotcids).ToArray();
		var addcusts = await context.Customers.Where(c => addcids.Contains(c.Id)).ToArrayAsync().ConfigureAwait(false);
		foreach (var addcust in addcusts)
		{
			Log($"I ConnectRealmChildCustomers | Realm {realm.Id} {realm.Name} ADD Customer {addcust.Id} {addcust.Name}");
			realm.Customers.Add(addcust);
		}
		await context.SaveChangesAsync().ConfigureAwait(false);
		return await RereadRealm(context, id);
	}

	public async Task<Realm?> ReplaceRealmChildCustomers(string realmId, string[] customerIds)
	{
		int id = int.Parse(realmId);
		using var context = MakeContext();
		var realm = await context.Realms.Include(r => r.Customers).FirstOrDefaultAsync(r => r.Id == id).ConfigureAwait(false);
		if (realm == null) return null;
		int[] cids = customerIds.Select(c => int.Parse(c)).ToArray();
		var addcusts = await context.Customers.Where(c => cids.Contains(c.Id)).ToArrayAsync().ConfigureAwait(false);
		realm.Customers.Clear();
		foreach (var addcust in addcusts)
		{
			realm.Customers.Add(addcust);
		}
		await context.SaveChangesAsync().ConfigureAwait(false);
		return await RereadRealm(context, id);
	}

	static async Task<Realm?> RereadRealm(EF.ExampleContext context, int realmId)
	{
		var realm = await context.Realms.AsNoTracking().Include(r => r.Users).Include(r => r.Users).FirstOrDefaultAsync(r => r.Id == realmId).ConfigureAwait(false);
		return ToRealm(realm, true);
	}
}
