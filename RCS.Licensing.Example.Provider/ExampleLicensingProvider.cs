using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using RCS.Licensing.Provider.Shared;
using RCS.Licensing.Provider.Shared.Entities;
using EF = RCS.Licensing.Example.Provider.EFCore;

namespace RCS.Licensing.Example.Provider;

/// <summary>
/// <para>
/// An example of a licensing provider that implements the full <see cref="ILicensingProvider"/> interface defined
/// by the Carbon cross-tabulation product suite. This provider uses a SQL Server datbase as the backing storage
/// for licensing records of user accounts, customers, and jobs.
/// </para>
/// <para>
/// This object is free for developers to use or modify to suit their company requiements. The code is designed
/// to be general-purpose so it can hopefully be used in different hosting environments by only changing the
/// database connection string passed into the provider's constructor.
/// </para>
/// </summary>
public partial class ExampleLicensingProvider : ILicensingProvider
{
	readonly string _connect;
	readonly string? _productKey;

	/// <summary>
	/// Constructs an example licensing service provider. Note that the four parameters <paramref name="subscriptionId"/>, <paramref name="tenantId"/>,
	/// <paramref name="applicationId"/> and <paramref name="applicationSecret"/> are optional, but they must all be specified to allow the provider to
	/// create, modify and delete Azure Storage Accounts which correspond to licensing customers.
	/// See the notes on <see cref="UpdateCustomer(Shared.Entities.Customer)"/> for more information.
	/// </summary>
	/// <param name="adoConnectionString">ADO.NET connections string to the SQL server database containing the licensing information.</param>
	/// <param name="productKey">
	/// If this licensing provider is going to be used anywhere in a stack of applications that use the Carbon cross-tabulation engine,
	/// then a product key supplied by <a href="https://www.redcentresoftware.com/" target="_blank">Red Centre Software</a> must be passed
	/// into the provider so that it can be passed back in authentication responses. The Carbon engine calls licensing providers (including
	/// this one), and it expects the provider to return a valid product key.
	/// </param>
	/// <exception cref="ArgumentNullException">Thrown if the <paramref name="adoConnectionString"/> is null.</exception>
	public ExampleLicensingProvider(string adoConnectionString, string? productKey = null)
	{
		_connect = adoConnectionString ?? throw new ArgumentNullException(nameof(adoConnectionString));
		_productKey = productKey;
	}

	public event EventHandler<string>? ProviderLog;

	public string Name
	{
		get
		{
			var t = GetType();
			var asm = t.Assembly;
			var an = asm.GetName();
			return asm.GetCustomAttribute<AssemblyTitleAttribute>()!.Title;
		}
	}

	public string Description
	{
		get
		{
			var t = GetType();
			var asm = t.Assembly;
			var an = asm.GetName();
			return asm.GetCustomAttribute<AssemblyDescriptionAttribute>()!.Description;
		}
	}

	public string ConfigSummary
	{
		get
		{
			var m = Regex.Match(_connect, "(?:Server|Data Source)=([^;]+)", RegexOptions.IgnoreCase);
			string server = m.Success ? m.Groups[1].Value : "(SERVER?)";
			m = Regex.Match(_connect, "(?:database|initial catalog)=([^;]+)", RegexOptions.IgnoreCase);
			string database = m.Success ? m.Groups[1].Value : "(DATABASE?)";
			return $"{server};{database}";
		}
	}

	void Log(string message) => ProviderLog?.Invoke(this, message);

	static string Join(params object[]? values) => values == null ? "NULL" : "[" + string.Join(",", values) + "]";

	EF.ExampleContext MakeContext() => new(_connect);

	#region Convert SQL Entities to Shared Entities

	/// <summary>
	/// A deep loaded User from the example database is converted into a Carbon full licence.
	/// </summary>
	async Task<LicenceFull> UserToFull(EF.User user)
	{
		var custcomp = new CustomerComparer();
		EF.Customer[] jobcusts = [.. user.Jobs.Where(j => j.Customer != null).Select(j => j.Customer!)];
		EF.Customer[] allcusts = [.. user.Customers.Concat(jobcusts).Distinct(custcomp)];
		EF.Job[] custjobs = [.. user.Customers.SelectMany(c => c.Jobs)];
		var jobcomp = new JobComparer();
		EF.Job[] alljobs = [.. user.Jobs.Concat(custjobs).Distinct(jobcomp)];
		string s = $"{user.Id}+{user.Name}+{DateTime.UtcNow:s}";
		var licfull = new LicenceFull()
		{
			Id = user.Id.ToString(),
			Name = user.Name,
			CloudCustomerNames = user.CloudCustomerNames?.Split([.. ",; "]),
			CloudJobNames = user.JobNames?.Split([.. ",; "]),
			DashboardNames = user.DashboardNames?.Split([.. ",; "]),
			VartreeNames = user.VartreeNames?.Split([.. ",; "]),
			Created = user.Created,
			DataLocation = ((DataLocationType?)user.DataLocation)?.ToString(),
			EntityId = user.EntityId,
			Filter = user.Filter,
			LoginCount = user.LoginCount,
			LoginMax = user.LoginMax,
			LoginMacs = user.LoginMacs,
			MinVersion = user.MinVersion,
			Version = user.Version,
			Sequence = user.Sequence,
			Sunset = user.Sunset,
			Email = user.Email,
			Comment = user.Comment,
			LastLogin = DateTime.UtcNow,
			DaysRemaining = null,
			EntityLogo = null,
			EntityName = null,
			EntityType = null,
			Recovered = null,
			GuestJobs = null,
			ProductKey = _productKey,
			LicenceSignature = null,
			Roles = user.Roles?.Split([.. ",; "]),
			Realms = [.. user.Realms.Select(r => new LicenceRealm() { Id = r.Id.ToString(), Name = r.Name, Inactive = r.Inactive, Policy = r.Policy })],
			Customers = [.. allcusts.Select(c => new LicenceCustomer()
			{
				Id = c.Id.ToString(),
				Name = c.Name,
				DisplayName = c.DisplayName,
				Comment = c.Comment,
				StorageKey = c.StorageKey,
				Info = c.Info,
				Logo = c.Logo,
				SignInLogo = c.SignInLogo,
				SignInNote = c.SignInNote,
				Sequence = c.Sequence,
				Url = null,
				AgencyId = null,
				ParentAgency = null,
				Jobs = [.. alljobs.Where(j => j.CustomerId == c.Id).Select(j => new LicenceJob()
				{
					Id = j.Id.ToString(),
					Name = j.Name,
					DisplayName = j.DisplayName,
					Description = j.Description,
					Info = j.Info,
					Logo = j.Logo,
					Sequence = j.Sequence,
					Url = j.Url,
					VartreeNames = j.VartreeNames?.Split([.. ",; "])
					//RealCloudVartreeNames -> Filled by loop below
					//IsAccessible -> Filled by loop below
				})]
			})]
		};

		// The real vartree names are added to the licensing response. This is the only place
		// where licensing does processing outside its own data. The names of real vartree (*.vtr)
		// blobs can only be found by scanning the root blobs in each job's container, which is
		// done in parallel to minimise delays.

		var tasks = licfull.Customers
			.Where(c => c.StorageKey != null)
			.SelectMany(c => c.Jobs.Select(j => new { c, j }))
			.Select(x => ScanJobForVartreesAsync(x.c.StorageKey, x.j));
		await Task.WhenAll(tasks);

		return licfull;
	}

	static async Task ScanJobForVartreesAsync(string storageConnect, LicenceJob job)
	{
		var cc = new BlobContainerClient(storageConnect, job.Name);
		// A job's container does not contain many root blobs where the vartrees are stored.
		// A single call is expected to return all root blobs without the need for continue token looping.
		IAsyncEnumerable<Page<BlobHierarchyItem>> pages = cc.GetBlobsByHierarchyAsync(delimiter: "/", prefix: null).AsPages(null);
		var list = new List<string>();
		try
		{
			await foreach (Page<BlobHierarchyItem> page in pages)
			{
				foreach (BlobHierarchyItem bhi in page.Values.Where(b => b.IsBlob))
				{
					string blobext = Path.GetExtension(bhi.Blob.Name);
					if (string.Compare(blobext, ".vtr", StringComparison.OrdinalIgnoreCase) == 0)
					{
						list.Add(Path.GetFileNameWithoutExtension(bhi.Blob.Name));
					}
				}
			}
			job.IsAccessible = true;
		}
		catch (RequestFailedException ex)
		{
			Trace.WriteLine($"@@@@ ERROR Status {ex.Status} ErrorCode {ex.ErrorCode} - {ex.Message}");
			job.IsAccessible = false;
		}
		job.RealCloudVartreeNames = [.. list];
	}

	static Customer? ToCustomer(EF.Customer? cust, bool includeChildren)
	{
		if (cust == null) return null;
		return new()
		{
			Id = cust.Id.ToString(),
			Name = cust.Name,
			DisplayName = cust.DisplayName,
			Psw = cust.Psw,
			StorageKey = cust.StorageKey,
			CloudCustomerNames = cust.CloudCustomerNames?.Split(',') ?? [],
			DataLocation = (DataLocationType?)cust.DataLocation ?? DataLocationType.Cloud,
			Sequence = cust.Sequence,
			Corporation = cust.Corporation,
			Comment = cust.Comment,
			Info = cust.Info,
			Logo = cust.Logo,
			SignInLogo = cust.SignInLogo,
			SignInNote = cust.SignInNote,
			Credits = cust.Credits,
			Spent = cust.Spent,
			Sunset = cust.Sunset,
			MaxJobs = cust.MaxJobs,
			Inactive = cust.Inactive,
			Created = cust.Created,
			Jobs = includeChildren ? cust.Jobs?.Select(j => ToJob(j, false)).ToArray() : null,
			Users = includeChildren ? cust.Users?.Select(u => ToUser(u, false)).ToArray() : null,
			Realms = includeChildren ? cust.Realms?.Select(r => ToRealm(r, false)).ToArray() : null
		};
	}

	static Job? ToJob(EF.Job? job, bool includeChildren)
	{
		if (job == null) return null;
		return new()
		{
			Id = job.Id.ToString(),
			Name = job.Name,
			DataLocation = (DataLocationType?)job.DataLocation ?? DataLocationType.Cloud,
			DisplayName = job.DisplayName,
			Description = job.Description,
			Cases = job.Cases,
			Logo = job.Logo,
			Info = job.Info,
			Inactive = job.Inactive,
			Created = job.Created,
			IsMobile = job.IsMobile,
			DashboardsFirst = job.DashboardsFirst,
			LastUpdate = job.LastUpdate,
			Sequence = job.Sequence,
			Url = job.Url,
			CustomerId = job.CustomerId?.ToString(),
			VartreeNames = job.VartreeNames?.Split(',') ?? [],
			Users = includeChildren ? job.Users?.Select(u => ToUser(u, false)).ToArray() : null,
			Customer = includeChildren ? ToCustomer(job.Customer, false) : null
		};
	}

	static User? ToUser(EF.User? user, bool includeChildren)
	{
		if (user == null) return null;
		return new()
		{
			Id = user.Id.ToString(),
			Name = user.Name,
			ProviderId = user.ProviderId,

			// ┌──────────────────────────────────────────────────────────────────────────┐
			// │  This example provider does not roundtrip the plaintext password for     │
			// │  obvious security reasons. It does even store the plaintext password     │
			// │  in the database. This property only exists for backwards compatibility  │
			// │  with a legacy licensing system. The Psw value is used when upserting a  │
			// │  User to indicate a new password is being requested, but that's its      │
			// │  only use.                                                               │
			// └──────────────────────────────────────────────────────────────────────────┘
			//Psw = user.Psw,
			PassHash = user.PassHash,
			Email = user.Email,
			EntityId = user.EntityId,
			CloudCustomerNames = user.CloudCustomerNames?.Split(",; ".ToCharArray(), StringSplitOptions.RemoveEmptyEntries) ?? [],
			JobNames = user.JobNames?.Split(",; ".ToCharArray(), StringSplitOptions.RemoveEmptyEntries) ?? [],
			VartreeNames = user.VartreeNames?.Split(",; ".ToCharArray(), StringSplitOptions.RemoveEmptyEntries) ?? [],
			DashboardNames = user.DashboardNames?.Split(",; ".ToCharArray(), StringSplitOptions.RemoveEmptyEntries) ?? [],
			DataLocation = (DataLocationType?)user.DataLocation ?? DataLocationType.Cloud,
			Sequence = user.Sequence,
			Uid = user.Uid,
			Comment = user.Comment,
			RoleSet = user.Roles?.Split(",; ".ToCharArray(), StringSplitOptions.RemoveEmptyEntries) ?? [],
			Filter = user.Filter,
			LoginMacs = user.LoginMacs,
			LoginCount = user.LoginCount,
			LoginMax = user.LoginMax,
			LastLogin = user.LastLogin,
			Sunset = user.Sunset,
			MaxJobs = user.MaxJobs,
			Version = user.Version,
			MinVersion = user.MinVersion,
			IsDisabled = user.IsDisabled,
			Created = user.Created,
			Realms = includeChildren ? user.Realms?.Select(r => ToRealm(r, false)).ToArray() : null,
			Customers = includeChildren ? user.Customers?.Select(c => ToCustomer(c, false)).ToArray() : null,
			Jobs = includeChildren ? user.Jobs?.Select(j => ToJob(j, false)).ToArray() : null
		};
	}

	static Realm? ToRealm(EF.Realm? realm, bool includeChildren)
	{
		if (realm == null) return null;
		return new Realm()
		{
			Id = realm.Id.ToString(),
			Name = realm.Name,
			Inactive = realm.Inactive,
			Policy = realm.Policy,
			Created = realm.Created,
			Users = includeChildren ? realm.Users?.Select(u => ToUser(u, false)).ToArray() : null,
			Customers = includeChildren ? realm.Customers?.Select(c => ToCustomer(c, false)).ToArray() : null
		};
	}

	#endregion
}
