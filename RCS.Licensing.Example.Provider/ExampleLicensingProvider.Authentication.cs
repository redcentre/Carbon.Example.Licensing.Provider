using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RCS.Licensing.Provider.Shared;

namespace RCS.Licensing.Example.Provider;

partial class ExampleLicensingProvider
{
	const string GuestAccountName = "guest";

	static long GetId(string userId) => long.TryParse(userId, out long id) ? id : throw new ExampleLicensingException(LicensingErrorType.IdentityBadFormat, $"User Id '{userId}' is not in the correct format");

	public async Task<LicenceFull> AuthenticateId(string userId, string? password, bool skipCache = false)
	{
		using var context = MakeContext();
		long id = GetId(userId);
		var user = await context.Users
			.Include(u => u.Customers).ThenInclude(c => c.Jobs)
			.Include(u => u.Jobs).ThenInclude(j => j.Customer)
			.Include(u => u.Realms)
			.FirstOrDefaultAsync(u => u.Id == id) ?? throw new ExampleLicensingException(LicensingErrorType.IdentityNotFound, $"User Id '{userId}' does not exist");
		if (user.PassHash != null)
		{
			// If user's password hash is null, then it's the rare and possibly invalid
			// situation where a user does not have a password and can authenticate without one.
			// Normally the hash will be present and it must be compared to the hash of the incoming password.
			byte[] inhash = DeepHash(password ?? "", user.Uid)!;
			if (!inhash.SequenceEqual(user.PassHash)) throw new ExampleLicensingException(LicensingErrorType.PasswordIncorrect, $"User Id '{userId}' incorrect password");
		}
		user.LoginCount ??= 1;
		user.LastLogin = DateTime.UtcNow;
		await context.SaveChangesAsync().ConfigureAwait(false);
		return await UserToFull(user);
	}

	public async Task<LicenceFull> AuthenticateName(string userName, string? password, bool skipCache = false)
	{
		using var context = MakeContext();
		var user = await context.Users
			.Include(u => u.Customers).ThenInclude(c => c.Jobs)
			.Include(u => u.Jobs).ThenInclude(j => j.Customer)
			.Include(u => u.Realms)
			.ToAsyncEnumerable()
			.FirstOrDefaultAsync(u => string.Compare(u.Name, userName, StringComparison.CurrentCultureIgnoreCase) == 0) ?? throw new ExampleLicensingException(LicensingErrorType.IdentityNotFound, $"User Name '{userName}' does not exist");
		if (user.PassHash != null)
		{
			byte[] inhash = DeepHash(password ?? "", user.Uid)!;
			if (!inhash.SequenceEqual(user.PassHash)) throw new ExampleLicensingException(LicensingErrorType.PasswordIncorrect, $"User Name '{userName}' incorrect password");
		}
		user.LoginCount ??= 1;
		user.LastLogin = DateTime.UtcNow;
		await context.SaveChangesAsync().ConfigureAwait(false);
		return await UserToFull(user);
	}

	public async Task<LicenceFull> GetFreeLicence(string? clientIdentifier = null, bool skipCache = false)
	{
		using var context = MakeContext();
		var user = await context.Users
			.Include(u => u.Customers).ThenInclude(c => c.Jobs)
			.Include(u => u.Jobs).ThenInclude(j => j.Customer)
			.FirstOrDefaultAsync(u => u.Name == GuestAccountName) ?? throw new ExampleLicensingException(LicensingErrorType.IdentityNotFound, $"Free or guest account with Name {GuestAccountName} does not exist");
		user.LoginCount = user.LoginCount == null ? 1 : user.LoginCount + 1;
		user.LastLogin = DateTime.UtcNow;
		await context.SaveChangesAsync().ConfigureAwait(false);
		return await UserToFull(user);
	}

	public async Task<int> ChangePassword(string userId, string? oldPassword, string newPassword)
	{
		using var context = MakeContext();
		long id = GetId(userId);
		var user = await context.Users.FirstOrDefaultAsync(u => u.Id == id).ConfigureAwait(false) ?? throw new ExampleLicensingException(LicensingErrorType.IdentityNotFound, $"User Id '{userId}' does not exist");
		if (oldPassword != null)
		{
			// If an old password is specified then its hash must match the user's record hash.
			// Not specifying and old password causes the password to be replaced without verification.
			// The plaintext password is no longer persisted anywhere for modern safety reasons.
			byte[] inhash = DeepHash(oldPassword, user.Uid)!;
			if (!inhash.SequenceEqual(user.PassHash ?? [])) throw new ExampleLicensingException(LicensingErrorType.PasswordIncorrect, $"User Id '{userId}' incorrect old password");
		}
		user.PassHash = DeepHash(newPassword, user.Uid);
		user.Psw = null;
		return await context.SaveChangesAsync().ConfigureAwait(false);
	}

	[DoesNotReturn]
	public Task<bool> ResetPassword(string email, DateTime utcTime, int signature)
	{
		throw new NotImplementedException("ResetPassword cannot be implemented until legascy plaintext passwords are eliminated from the licensingn database.");
	}

	public async Task<int> UpdateAccount(string userId, string userName, string? comment, string? email)
	{
		using var context = MakeContext();
		long id = GetId(userId);
		var user = await context.Users.FirstOrDefaultAsync(u => u.Id == id).ConfigureAwait(false) ?? throw new ExampleLicensingException(LicensingErrorType.IdentityNotFound, $"User Id '{userId}' does not exist");
		user.Name = userName;
		user.Comment = comment;
		user.Email = email;
		return await context.SaveChangesAsync().ConfigureAwait(false);
	}
}
