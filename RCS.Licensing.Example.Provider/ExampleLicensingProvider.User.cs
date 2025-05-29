using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RCS.Licensing.Provider.Shared;
using RCS.Licensing.Provider.Shared.Entities;
using EF = RCS.Licensing.Example.Provider.EFCore;

namespace RCS.Licensing.Example.Provider;

partial class ExampleLicensingProvider
{
	public async Task<User?> ReadUser(string userId)
	{
		int id = int.Parse(userId);
		using var context = MakeContext();
		var user = await context.Users
			.AsNoTracking()
			.Include(u => u.Customers)
			.Include(u => u.Jobs)
			.Include(u => u.Realms)
			.FirstOrDefaultAsync(u => u.Id == id)
			.ConfigureAwait(false);
		return ToUser(user, true);
	}

	public async Task<User[]> ReadUsersByName(string userName)
	{
		using var context = MakeContext();
		return await context.Users.AsNoTracking()
			.Include(u => u.Realms)
			.Include(u => u.Customers)
			.Include(u => u.Jobs)
			.Where(u => u.Name == userName)
			.AsAsyncEnumerable()
			.Select(u => ToUser(u, true)!)
			.ToArrayAsync()
			.ConfigureAwait(false);
	}

	public async Task<User[]> ListUsers()
	{
		using var context = MakeContext();
		var users = await context.Users
			.AsNoTracking()
			.ToArrayAsync()
			.ConfigureAwait(false);
		return [.. users.Select(u => ToUser(u, false)!)];
	}

	public async Task<User[]> ListUsers(params string[]? realmIds)
	{
		int[] rids = realmIds?.Select(x => int.Parse(x)).ToArray() ?? [];
		using var context = MakeContext();
		return await context.Users
			.AsNoTracking()
			.Include(u => u.Realms)
			.Where(u => rids.Length == 0 || u.Realms.Any(r => rids.Contains(r.Id)))
			.AsAsyncEnumerable()
			.Select(u => ToUser(u, false)!)
			.ToArrayAsync()
			.ConfigureAwait(false);
	}

	public async Task<UserPick[]> ListUserPicksForRealms(params string[]? realmIds)
	{
		int[] rids = realmIds?.Select(x => int.Parse(x)).ToArray() ?? [];
		using var context = MakeContext();
		return await context.Users
			.AsNoTracking()
			.Include(u => u.Realms)
			.Where(u => rids.Length == 0 || u.Realms.Any(r => rids.Contains(r.Id)))
			.AsAsyncEnumerable()
			.Select(u => new UserPick(u.Id.ToString(), u.Name)
			{
				Email = u.Email,
				IsInactive = u.IsDisabled,
				Sunset = u.Sunset,
				Roles = u.Roles?.Split(",; ".ToCharArray(), StringSplitOptions.RemoveEmptyEntries) ?? []
			})
			.ToArrayAsync()
			.ConfigureAwait(false);
	}

	public async Task<UpsertResult<User>> UpsertUser(User user)
	{
		using var context = MakeContext();
		EF.User row;
		if (user.Id == null)
		{
			// A null Id on the incoming User has special meaning and indicates
			// we are creating a new User. The Id cannot be manually specified.
			int? newid = null;
			while (newid == null)
			{
				int tryid = Random.Shared.Next(10_000_000, 20_000_000);
				if (!await context.Users.AnyAsync(u => u.Id == tryid).ConfigureAwait(false))
				{
					newid = tryid;
				}
			}
			row = new EF.User
			{
				Id = newid.Value,
				Uid = Guid.NewGuid(),
				Created = DateTime.UtcNow
			};
			context.Users.Add(row);
		}
		else
		{
			EF.User? oldrow = await context.Users.FirstAsync(u => u.Id.ToString() == user.Id);
			if (oldrow == null)
			{
				return new UpsertResult<User>(null, UpsertStatusCode.NotFound, $"User Id {user.Id} not found for update");
			}
			row = oldrow;
		}
		row.Name = user.Name;
		row.ProviderId = user.ProviderId;
		if (user.Psw != null)
		{
			// ┌───────────────────────────────────────────────────────────────┐
			// │  If the incoming password is specified then it's expected to  │
			// │  be a request to change the password. In this case a fresh    │
			// │  hash is calculated. Note that the plaintext password is no   │
			// │  longer saved in the User record, only the hash, to conform   │
			// │  to modern safety conventions. At some point, all the plain   │
			// │  passwords in all User records will be erased.                │
			// └───────────────────────────────────────────────────────────────┘
			//row.Psw = user.Psw;
			row.PassHash = HP(user.Psw, row.Uid);
		}
		row.Email = user.Email;
		row.EntityId = user.EntityId;
		row.CloudCustomerNames = user.CloudCustomerNames?.Length > 0 ? string.Join(" ", user.CloudCustomerNames) : null;
		row.JobNames = user.JobNames?.Length > 0 ? string.Join(" ", user.JobNames) : null;
		row.VartreeNames = user.VartreeNames?.Length > 0 ? string.Join(" ", user.VartreeNames) : null;
		row.DashboardNames = user.DashboardNames?.Length > 0 ? string.Join(" ", user.DashboardNames) : null;
		row.DataLocation = (int?)user.DataLocation;
		row.Sequence = user.Sequence;
		row.Comment = user.Comment;
		row.Roles = user.Roles?.Length > 0 ? string.Join(" ", user.Roles) : null;
		row.Filter = user.Filter;
		row.LoginMacs = user.LoginMacs;
		row.LoginCount = user.LoginCount;
		row.LoginMax = user.LoginMax;
		row.LastLogin = user.LastLogin;
		row.Sunset = user.Sunset;
		row.MaxJobs = user.MaxJobs;
		row.Version = user.Version;
		row.MinVersion = user.MinVersion;
		row.IsDisabled = user.IsDisabled;
		await context.SaveChangesAsync().ConfigureAwait(false);
		row = await context.Users.AsNoTracking().Include(u => u.Customers).Include(u => u.Jobs).FirstAsync(u => u.Id == row.Id);
		User? upuser = await RereadUser(context, row.Id).ConfigureAwait(false);
		return new UpsertResult<User>(upuser, user.Id == null ? UpsertStatusCode.Inserted : UpsertStatusCode.Updated);
	}

	public async Task<int> DeleteUser(string userId)
	{
		int id = int.Parse(userId);
		using var context = MakeContext();
		EF.User? user = await context.Users
			.Include(u => u.Customers)
			.Include(u => u.Jobs)
			.Include(u => u.Realms)
			.FirstOrDefaultAsync(u => u.Id == id)
			.ConfigureAwait(false);
		if (user == null) return 0;
		foreach (var job in user.Jobs.ToArray())
		{
			user.Jobs.Remove(job);
		}
		foreach (var cust in user.Customers.ToArray())
		{
			user.Customers.Remove(cust);
		}
		foreach (var realm in user.Realms.ToArray())
		{
			user.Realms.Remove(realm);
		}
		context.Users.Remove(user);
		return await context.SaveChangesAsync().ConfigureAwait(false);
	}

	public async Task<User?> DisconnectUserChildCustomer(string userId, string customerId)
	{
		Log($"D DisconnectUserChildCustomer({userId},{customerId})");
		int uid = int.Parse(userId);
		using var context = MakeContext();
		EF.User? user = await context.Users.Include(u => u.Customers).FirstOrDefaultAsync(u => u.Id == uid).ConfigureAwait(false);
		if (user == null) return null;
		int cid = int.Parse(customerId);
		var cust = user.Customers.FirstOrDefault(c => c.Id == cid);
		if (cust != null)
		{
			Log($"I DisconnectUserChildCustomer | User {user.Id} {user.Name} DEL Cust {cust.Id} {cust.Name}");
			user.Customers.Remove(cust);
		}
		await context.SaveChangesAsync().ConfigureAwait(false);
		return await RereadUser(context, uid);
	}

	public async Task<User?> ConnectUserChildCustomers(string userId, string[] customerIds)
	{
		Log($"D ConnectUserChildCustomers({userId},{Join(customerIds)})");
		int uid = int.Parse(userId);
		using var context = MakeContext();
		EF.User? user = await context.Users.Include(u => u.Customers).Include(u => u.Jobs).FirstOrDefaultAsync(u => u.Id == uid).ConfigureAwait(false);
		if (user == null) return null;
		int[] cids = [.. customerIds.Select(x => int.Parse(x))];
		int[] gotcids = [.. user.Customers.Select(c => c.Id)];
		int[] addcids = [.. cids.Except(gotcids)];
		EF.Customer[] addcusts = await context.Customers.Where(c => addcids.Contains(c.Id)).ToArrayAsync().ConfigureAwait(false);
		foreach (var addcust in addcusts)
		{
			Log($"I ConnectUserChildCustomers | User {user.Id} {user.Name} Add Customer {addcust.Id} {addcust.Name}");
			user.Customers.Add(addcust);
		}
		await context.SaveChangesAsync().ConfigureAwait(false);
		return await RereadUser(context, uid);
	}

	public async Task<User?> ReplaceUserChildCustomers(string userId, string[] customerIds)
	{
		Log($"D ReplaceUserChildCustomers({userId},{Join(customerIds)})");
		int uid = int.Parse(userId);
		using var context = MakeContext();
		EF.User? user = await context.Users.Include(u => u.Customers).Include(u => u.Jobs).FirstOrDefaultAsync(u => u.Id == uid).ConfigureAwait(false);
		if (user == null) return null;
		int[] cids = [.. customerIds.Select(x => int.Parse(x))];
		EF.Customer[] addcusts = await context.Customers.Where(c => cids.Contains(c.Id)).ToArrayAsync().ConfigureAwait(false);
		user.Customers.Clear();
		foreach (var addcust in addcusts)
		{
			Log($"I ReplaceUserChildCustomers | User {user.Id} {user.Name} ADD Cust {addcust.Id} {addcust.Name}");
			user.Customers.Add(addcust);
		}
		await context.SaveChangesAsync().ConfigureAwait(false);
		return await RereadUser(context, uid);
	}

	public async Task<User?> DisconnectUserChildJob(string userId, string jobId)
	{
		Log($"D DisconnectUserChildJob({userId},{jobId})");
		int uid = int.Parse(userId);
		using var context = MakeContext();
		EF.User? user = await context.Users.Include(c => c.Jobs).FirstOrDefaultAsync(u => u.Id == uid).ConfigureAwait(false);
		if (user == null) return null;
		int jid = int.Parse(jobId);
		var job = user.Jobs.FirstOrDefault(j => j.Id == jid);
		if (job != null)
		{
			Log($"I DisconnectUserChildJob | User {user.Id} {user.Name} DEL Job {job.Id} {job.Name}");
			user.Jobs.Remove(job);
		}
		await context.SaveChangesAsync().ConfigureAwait(false);
		return await RereadUser(context, uid);
	}

	public async Task<User?> ConnectUserChildJobs(string userId, string[] jobIds)
	{
		Log($"D ConnectUserChildJobs({userId},{Join(jobIds)})");
		int uid = int.Parse(userId);
		using var context = MakeContext();
		EF.User? user = await context.Users.Include(u => u.Jobs).FirstOrDefaultAsync(u => u.Id == uid).ConfigureAwait(false);
		if (user == null) return null;
		int[] jids = [.. jobIds.Select(j => int.Parse(j))];
		int[] gotjids = [.. user.Jobs.Select(j => j.Id)];
		int[] addjids = [.. jids.Except(gotjids)];
		EF.Job[] addjobs = await context.Jobs.Where(j => addjids.Contains(j.Id)).ToArrayAsync().ConfigureAwait(false);
		foreach (var addjob in addjobs)
		{
			Log($"I ConnectUserChildJobs | User {user.Id} {user.Name} ADD Job {addjob.Id} {addjob.Name}");
			user.Jobs.Add(addjob);
		}
		await context.SaveChangesAsync().ConfigureAwait(false);
		return await RereadUser(context, uid).ConfigureAwait(false);
	}

	public async Task<User?> ReplaceUserChildJobs(string userId, string[] jobIds)
	{
		Log($"D ReplaceUserChildJobs({userId},{Join(jobIds)})");
		int uid = int.Parse(userId);
		using var context = MakeContext();
		EF.User? user = await context.Users.Include(u => u.Jobs).FirstOrDefaultAsync(u => u.Id == uid).ConfigureAwait(false);
		if (user == null) return null;
		int[] jids = [.. jobIds.Select(j => int.Parse(j))];
		var addjobs = await context.Jobs.Include(j => j.Users).Include(j => j.Customer).Where(j => jids.Contains(j.Id)).ToArrayAsync().ConfigureAwait(false);
		user.Jobs.Clear();
		foreach (var addjob in addjobs)
		{
			Log($"I ReplaceUserChildJobs | User {user.Id} {user.Name} ADD Job {addjob.Id} {addjob.Name}");
			user.Jobs.Add(addjob);
		}
		await context.SaveChangesAsync().ConfigureAwait(false);
		return await RereadUser(context, uid).ConfigureAwait(false);
	}

	public async Task<User?> DisconnectUserChildRealm(string userId, string realmId)
	{
		Log($"D DisconnectUserChildRealm({userId},{realmId})");
		int uid = int.Parse(userId);
		using var context = MakeContext();
		EF.User? user = await context.Users.Include(c => c.Realms).FirstOrDefaultAsync(u => u.Id == uid).ConfigureAwait(false);
		if (user == null) return null;
		int rid = int.Parse(realmId);
		EF.Realm? realm = user.Realms.FirstOrDefault(r => r.Id == rid);
		if (realm != null)
		{
			Log($"I DisconnectUserChildRealm | User {user.Id} {user.Name} DEL Realm {realm.Id} {realm.Name}");
			user.Realms.Remove(realm);
		}
		await context.SaveChangesAsync().ConfigureAwait(false);
		return await RereadUser(context, uid).ConfigureAwait(false);
	}

	public async Task<User?> ConnectUserChildRealms(string userId, string[] realmIds)
	{
		Log($"D ConnectUserChildRealms({userId},{Join(realmIds)})");
		int uid = int.Parse(userId);
		using var context = MakeContext();
		EF.User? user = await context.Users.Include(c => c.Realms).FirstOrDefaultAsync(u => u.Id == uid).ConfigureAwait(false);
		if (user == null) return null;
		int[] rids = [.. realmIds.Select(x => int.Parse(x))];
		int[] gotrids = [.. user.Realms.Select(r => r.Id)];
		int[] addrids = [.. rids.Except(gotrids)];
		EF.Realm[] addrealms = await context.Realms.Where(r => addrids.Contains(r.Id)).ToArrayAsync().ConfigureAwait(false);
		foreach (var addrealm in addrealms)
		{
			Log($"I ConnectUserChildRealms | User {user.Id} {user.Name} ADD Realm {addrealm.Id} {addrealm.Name}");
			user.Realms.Add(addrealm);
		}
		await context.SaveChangesAsync().ConfigureAwait(false);
		return await RereadUser(context, uid).ConfigureAwait(false);
	}

	public async Task<User?> ReplaceUserChildRealms(string userId, string[] realmIds)
	{
		Log($"D ReplaceUserChildRealms({userId},{Join(realmIds)})");
		int uid = int.Parse(userId);
		using var context = MakeContext();
		EF.User? user = await context.Users.Include(c => c.Realms).FirstOrDefaultAsync(u => u.Id == uid).ConfigureAwait(false);
		if (user == null) return null;
		int[] rids = [.. realmIds.Select(x => int.Parse(x))];
		EF.Realm[] addrealms = await context.Realms.Where(r => rids.Contains(r.Id)).ToArrayAsync().ConfigureAwait(false);
		user.Realms.Clear();
		foreach (var addrealm in addrealms)
		{
			Log($"I ReplaceUserChildRealms | User {user.Id} {user.Name} ADD Realm {addrealm.Id} {addrealm.Name}");
			user.Realms.Add(addrealm);
		}
		await context.SaveChangesAsync().ConfigureAwait(false);
		return await RereadUser(context, uid).ConfigureAwait(false);
	}

	static async Task<User?> RereadUser(EF.ExampleContext context, int userId)
	{
		var cust = await context.Users.AsNoTracking().Include(c => c.Customers).Include(c => c.Jobs).Include(c => c.Realms).FirstOrDefaultAsync(u => u.Id == userId).ConfigureAwait(false);
		return ToUser(cust, true);
	}

	[return: NotNullIfNotNull(nameof(p))]
	static byte[]? HP(string? p, Guid u)
	{
		if (p == null) return null;
		using var deriver = new Rfc2898DeriveBytes(p, u.ToByteArray(), 15000, HashAlgorithmName.SHA1);
		return deriver.GetBytes(16);
	}
}
