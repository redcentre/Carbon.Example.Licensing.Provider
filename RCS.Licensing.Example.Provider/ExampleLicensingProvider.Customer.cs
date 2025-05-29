using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using RCS.Licensing.Provider.Shared;
using RCS.Licensing.Provider.Shared.Entities;
using EF = RCS.Licensing.Example.Provider.EFCore;

namespace RCS.Licensing.Example.Provider;

partial class ExampleLicensingProvider
{
	public async Task<Customer?> ReadCustomer(string id)
	{
		using var context = MakeContext();
		var cust = await context.Customers.AsNoTracking()
			.Include(c => c.Users)
			.Include(c => c.Jobs)
			.Include(c => c.Realms)
			.FirstOrDefaultAsync(c => c.Id.ToString() == id)
			.ConfigureAwait(false);
		return ToCustomer(cust, true);
	}

	public async Task<Customer[]> ReadCustomersByName(string customerName)
	{
		using var context = MakeContext();
		return await context.Customers.AsNoTracking()
			.Include(c => c.Users)
			.Include(c => c.Jobs)
			.Include(c => c.Realms)
			.Where(c => c.Name == customerName)
			.AsAsyncEnumerable()
			.Select(c => ToCustomer(c, true)!)
			.ToArrayAsync()
			.ConfigureAwait(false);
	}

	public async Task<Customer[]> ListCustomers()
	{
		using var context = MakeContext();
		return await context.Customers.AsNoTracking()
			.AsAsyncEnumerable()
			.Select(c => ToCustomer(c, false)!)
			.ToArrayAsync()
			.ConfigureAwait(false);
	}

	public async Task<Customer[]> ListCustomers(params string[]? realmIds)
	{
		int[] rids = realmIds?.Select(x => int.Parse(x)).ToArray() ?? [];
		using var context = MakeContext();
		return await context.Customers
			.AsNoTracking()
			.Where(c => rids.Length == 0 || c.Realms.Any(r => rids.Contains(r.Id)))
			.AsAsyncEnumerable()
			.Select(c => ToCustomer(c, false)!)
			.ToArrayAsync()
			.ConfigureAwait(false);
	}

	public async Task<CustomerPick[]> ListCustomerPicksForRealms(params string[]? realmIds)
	{
		int[] rids = realmIds?.Select(x => int.Parse(x)).ToArray() ?? [];
		using var context = MakeContext();
		return await context.Customers
			.AsNoTracking()
			.Include(c => c.Jobs)
			.Include(c => c.Realms)
			.Where(c => rids.Length == 0 || c.Realms.Any(r => rids.Contains(r.Id)))
			.AsAsyncEnumerable()
			.Select(c => new CustomerPick(c.Id.ToString(), c.Name)
			{
				IsInactive = c.Inactive,
				DisplayName = c.DisplayName,
				Jobs = [.. c.Jobs.Select(j => new JobPick(j.Id.ToString(), j.Name)
				{
					DisplayName = j.DisplayName,
					IsInactive = j.Inactive
				})]
			})
			.ToArrayAsync()
			.ConfigureAwait(false);
	}

	/// <summary>
	/// Updates or inserts a customer. See the remarks for more detailed information.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This method should have been called UpsertCustomer because it can insert or update a customer.
	/// If the Id if the <paramref name="customer"/> parameter is <c>null</c> then a new customer will be created with a new unique Id.
	/// If a customer with the Id exists then it will be updated with the incoming values.
	/// An error is thrown if an attempt is made to update a customer with an Id that does not exist.
	/// </para>
	/// </remarks>
	/// <exception cref="ExampleLicensingException">Thrown if an attempt is made to update an existing customer Id that does not exist.</exception>
	public async Task<UpsertResult<Customer>> UpsertCustomer(Customer customer)
	{
		using var context = MakeContext();
		EF.Customer row;
		if (customer.Id == null)
		{
			// Inserting a new customer. Generate a new unique Id.
			int? newid = null;
			while (newid == null)
			{
				int tryid = Random.Shared.Next(30_000_000, 40_000_000);
				if (!await context.Customers.AnyAsync(c => c.Id == tryid).ConfigureAwait(false))
				{
					newid = tryid;
				}
			}
			row = new EF.Customer
			{
				Id = newid.Value,
				Created = DateTime.UtcNow
			};
			context.Customers.Add(row);
			// We could create an Azure Storage Account and Realm for the new customer here, but that
			// responsibility currently lies with the parent app (such as DNA or the web service).
		}
		else
		{
			// This is an update of an existing customer Id (which must exist).
			EF.Customer? oldrow = await context.Customers.FirstOrDefaultAsync(c => c.Id.ToString() == customer.Id).ConfigureAwait(false);
			if (oldrow == null)
			{
				return new UpsertResult<Customer>(null, UpsertStatusCode.NotFound, $"Customer Id {customer.Id} not found for update");
			}
			row = oldrow;
		}
		row.Name = customer.Name;
		row.DisplayName = customer.DisplayName;
		row.Psw = customer.Psw;
		row.StorageKey = customer.StorageKey;
		row.CloudCustomerNames = customer.CloudCustomerNames?.Length > 0 ? string.Join(" ", customer.CloudCustomerNames) : null;
		row.DataLocation = (int?)customer.DataLocation;
		row.Sequence = customer.Sequence;
		row.Corporation = customer.Corporation;
		row.Comment = customer.Comment;
		row.Info = customer.Info;
		row.Logo = customer.Logo;
		row.SignInLogo = customer.SignInLogo;
		row.SignInNote = customer.SignInNote;
		row.Credits = customer.Credits;
		row.Spent = customer.Spent;
		row.Sunset = customer.Sunset;
		row.MaxJobs = customer.MaxJobs;
		row.Inactive = customer.Inactive;
		await context.SaveChangesAsync().ConfigureAwait(false);
		Customer? cust = await RereadCustomer(context, row.Id).ConfigureAwait(false);
		return new UpsertResult<Customer>(cust, customer.Id == null ? UpsertStatusCode.Inserted : UpsertStatusCode.Updated);
	}

	public async Task<string[]> ValidateCustomer(string customerId)
	{
		using var context = MakeContext();
		EF.Customer? cust = await context.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id.ToString() == customerId).ConfigureAwait(false);
		var list = new List<string>();
		if (cust == null)
		{
			list.Add($"Customer Id {customerId} is not in the licensing database.");
		}
		else
		{
			try
			{
				var client = new BlobServiceClient(cust.StorageKey);
				var acc = await client.GetAccountInfoAsync();
				Trace.WriteLine($"Probe customer '{cust.Name}' storage account: {acc.Value.AccountKind}");
			}
			catch (Exception ex)
			{
				// The exception message can be really verbose with lots of lines.
				string first = ex.Message.Split("\r\n".ToCharArray(), StringSplitOptions.RemoveEmptyEntries).First();
				list.Add(first);
			}
		}
		return [.. list];
	}

	public async Task<int> DeleteCustomer(string id)
	{
		using var context = MakeContext();
		EF.Customer? cust = await context.Customers.Include(c => c.Jobs).Include(c => c.Users).Include(c => c.Realms).FirstOrDefaultAsync(c => c.Id == int.Parse(id)).ConfigureAwait(false);
		if (cust == null) return 0;
		foreach (var job in cust.Jobs.ToArray())
		{
			job.Customer = null;
			cust.Jobs.Remove(job);
			context.Jobs.Remove(job);
		}
		foreach (var user in cust.Users.ToArray())
		{
			cust.Users.Remove(user);
		}
		foreach (var realm in cust.Realms.ToArray())
		{
			cust.Realms.Remove(realm);
		}
		context.Customers.Remove(cust);
		return await context.SaveChangesAsync().ConfigureAwait(false);
	}

	public Task<Customer?> ConnectCustomerChildJobs(string customerId, string[] jobIds) => throw new Exception($"Changing customer and job relationships is not permitted after they have been created.");

	public Task<Customer?> DisconnectCustomerChildJob(string customerId, string jobId) => throw new Exception($"Changing customer and job relationships is not permitted after they have been created.");

	public Task<Customer?> ReplaceCustomerChildJobs(string customerId, string[] jobIds) => throw new Exception($"Changing customer and job relationships is not permitted after they have been created.");

	public async Task<Customer?> DisconnectCustomerChildUser(string customerId, string userId)
	{
		Log($"D DisconnectCustomerChildUser({customerId},{userId})");
		int id = int.Parse(customerId);
		using var context = MakeContext();
		EF.Customer? cust = await context.Customers.Include(c => c.Users).FirstOrDefaultAsync(c => c.Id == id).ConfigureAwait(false);
		if (cust == null) return null;
		var user = cust.Users.FirstOrDefault(j => j.Id.ToString() == userId);
		if (user != null)
		{
			Log($"I DisconnectCustomerChildUser | Cust {cust.Id} {cust.Name} DEL User {user.Id} {user.Name}");
			cust.Users.Remove(user);
		}
		await context.SaveChangesAsync().ConfigureAwait(false);
		return await RereadCustomer(context, id).ConfigureAwait(false);
	}

	public async Task<Customer?> ConnectCustomerChildUsers(string customerId, string[] userIds)
	{
		Log($"D ConnectCustomerChildUsers({customerId},{Join(userIds)})");
		int cid = int.Parse(customerId);
		using var context = MakeContext();
		EF.Customer? cust = await context.Customers.Include(c => c.Users).Include(c => c.Jobs).FirstOrDefaultAsync(c => c.Id == cid).ConfigureAwait(false);
		if (cust == null) return null;
		int[] uids = [.. userIds.Select(u => int.Parse(u))];
		int[] gotuids = [.. cust.Users.Select(u => u.Id)];
		int[] adduids = [.. uids.Except(gotuids)];
		EF.User[] addusers = await context.Users.Where(u => adduids.Contains(u.Id)).ToArrayAsync().ConfigureAwait(false);
		foreach (var adduser in addusers)
		{
			Log($"I ConnectCustomerChildUsers | Customer {cust.Id} {cust.Name} ADD User {adduser.Id} {adduser.Name}");
			cust.Users.Add(adduser);
		}
		await context.SaveChangesAsync().ConfigureAwait(false);
		return await RereadCustomer(context, cid).ConfigureAwait(false);
	}

	public async Task<Customer?> ReplaceCustomerChildUsers(string customerId, string[] userIds)
	{
		Log($"D ReplaceCustomerChildUsers({customerId},{Join(userIds)})");
		int cid = int.Parse(customerId);
		using var context = MakeContext();
		EF.Customer? cust = await context.Customers.Include(c => c.Users).FirstOrDefaultAsync(c => c.Id == cid);
		if (cust == null) return null;
		int[] uids = [.. userIds.Select(x => int.Parse(x))];
		EF.User[] addusers = await context.Users.Where(u => uids.Contains(u.Id)).ToArrayAsync().ConfigureAwait(false);
		cust.Users.Clear();
		foreach (var adduser in addusers)
		{
			Log($"I ReplaceCustomerChildUsers | User {cust.Id} {cust.Name} ADD User {adduser.Id} {adduser.Name}");
			cust.Users.Add(adduser);
		}
		await context.SaveChangesAsync().ConfigureAwait(false);
		return await RereadCustomer(context, cid).ConfigureAwait(false);
	}

	static async Task<Customer?> RereadCustomer(EF.ExampleContext context, int customerId)
	{
		var cust = await context.Customers.AsNoTracking().Include(c => c.Users).Include(c => c.Jobs).FirstOrDefaultAsync(c => c.Id == customerId).ConfigureAwait(false);
		return ToCustomer(cust, true);
	}
}
