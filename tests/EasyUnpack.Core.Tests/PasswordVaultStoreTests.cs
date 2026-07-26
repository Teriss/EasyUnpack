using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EasyUnpack.Core.Passwords;

namespace EasyUnpack.Core.Tests;

public sealed class PasswordVaultStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"EasyUnpackVaultTests-{Guid.NewGuid():N}");
    private string VaultPath => Path.Combine(_directory, "passwords.json");

    public PasswordVaultStoreTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task Plain_vault_round_trips_the_explicit_priority_order()
    {
        var vault = new PasswordVault();
        vault.RecordSuccessfulPassword("first");
        vault.RecordSuccessfulPassword("second", "资源站");
        Assert.True(vault.Move(vault.Entries[0].Id, 1));
        await PasswordVaultStore.SaveAsync(vault, VaultPath, null);

        var loaded = await PasswordVaultStore.LoadAsync(VaultPath, null);

        Assert.Equal(["first", "second"], loaded.GetCandidates());
        Assert.Equal("资源站", loaded.Entries.Single(entry => entry.Value == "second").Label);
    }

    [Fact]
    public void Passwords_can_be_added_edited_moved_and_promoted_after_success()
    {
        var vault = new PasswordVault();
        Assert.True(vault.Add("first", "旧备注"));
        Assert.True(vault.Add("second"));
        Assert.False(vault.Add("first"));
        Assert.Equal(["second", "first"], vault.GetCandidates());

        var first = vault.Entries.Single(entry => entry.Value == "first");
        Assert.True(vault.Update(first.Id, "updated", "新备注"));
        Assert.False(vault.Update(first.Id, "second", null));
        Assert.True(vault.Move(first.Id, 0));
        Assert.Equal(["updated", "second"], vault.GetCandidates());

        vault.RecordSuccessfulPassword("second");
        Assert.Equal(["second", "updated"], vault.GetCandidates());
        Assert.Equal(1, vault.Entries[0].SuccessCount);
    }

    [Fact]
    public async Task Legacy_array_payload_is_migrated_using_the_previous_recency_order()
    {
        var older = new PasswordEntry(Guid.NewGuid(), "older", null, DateTimeOffset.Parse("2025-01-01T00:00:00Z"), 5);
        var newer = new PasswordEntry(Guid.NewGuid(), "newer", null, DateTimeOffset.Parse("2026-01-01T00:00:00Z"), 1);
        var payload = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new[] { older, newer }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(VaultPath, JsonSerializer.Serialize(new { protection = "plain", payload }), Encoding.UTF8);

        var loaded = await PasswordVaultStore.LoadAsync(VaultPath, null);

        Assert.Equal(["newer", "older"], loaded.GetCandidates());
        await PasswordVaultStore.SaveAsync(loaded, VaultPath, null);
        Assert.Contains("\"version\": 2", Encoding.UTF8.GetString(Convert.FromBase64String(JsonDocument.Parse(await File.ReadAllTextAsync(VaultPath)).RootElement.GetProperty("payload").GetString()!)));
    }

    [Fact]
    public async Task Encrypted_vault_requires_the_correct_master_password()
    {
        var vault = new PasswordVault();
        vault.RecordSuccessfulPassword("secret-password");
        await PasswordVaultStore.SaveAsync(vault, VaultPath, "master-password");

        await Assert.ThrowsAsync<CryptographicException>(() => PasswordVaultStore.LoadAsync(VaultPath, "wrong"));
        var loaded = await PasswordVaultStore.LoadAsync(VaultPath, "master-password");

        Assert.Equal(["secret-password"], loaded.GetCandidates());
        Assert.DoesNotContain("secret-password", await File.ReadAllTextAsync(VaultPath));
    }

    [Fact]
    public async Task Encrypted_vault_round_trips_priority_statistics_and_labels()
    {
        var vault = new PasswordVault();
        vault.Add("low-priority", "备用");
        vault.RecordSuccessfulPassword("most-likely", "常用");
        vault.RecordSuccessfulPassword("most-likely");

        await PasswordVaultStore.SaveAsync(vault, VaultPath, "master-password");
        var loaded = await PasswordVaultStore.LoadAsync(VaultPath, "master-password");

        Assert.Equal(["most-likely", "low-priority"], loaded.GetCandidates());
        Assert.Equal("常用", loaded.Entries[0].Label);
        Assert.Equal(2, loaded.Entries[0].SuccessCount);
        Assert.NotNull(loaded.Entries[0].LastSuccessfulAt);
    }

    [Fact]
    public void Constructor_and_remove_keep_the_remaining_priority_order_stable()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var vault = new PasswordVault(
        [
            new PasswordEntry(firstId, "first", null, null, 0),
            new PasswordEntry(Guid.NewGuid(), "first", "duplicate", DateTimeOffset.UtcNow, 9),
            new PasswordEntry(secondId, "second", null, null, 0),
            new PasswordEntry(Guid.NewGuid(), "third", null, null, 0),
        ]);

        Assert.Equal(["first", "second", "third"], vault.GetCandidates());
        Assert.True(vault.Remove(secondId));
        Assert.Equal(["first", "third"], vault.GetCandidates());
        Assert.False(vault.Remove(secondId));
    }

    [Fact]
    public async Task GetProtection_identifies_plain_and_master_password_vaults()
    {
        var vault = new PasswordVault();
        await PasswordVaultStore.SaveAsync(vault, VaultPath, null);
        Assert.Equal(PasswordVaultProtection.Plain, await PasswordVaultStore.GetProtectionAsync(VaultPath));

        await PasswordVaultStore.SaveAsync(vault, VaultPath, "master-password");
        Assert.Equal(PasswordVaultProtection.MasterPassword, await PasswordVaultStore.GetProtectionAsync(VaultPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
