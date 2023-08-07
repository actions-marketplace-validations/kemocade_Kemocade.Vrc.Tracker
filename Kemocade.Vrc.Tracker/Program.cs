using CommandLine;
using Kemocade.Vrc.Tracker;
using Kemocade.Vrc.Tracker.Models;
using OtpNet;
using System.Text.Json;
using VRChat.API.Api;
using VRChat.API.Client;
using VRChat.API.Model;
using static Kemocade.Vrc.Tracker.Models.TrackedGroup;
using static System.Console;
using static System.IO.File;
using static System.Text.Json.JsonSerializer;

// Configure Cancellation
using CancellationTokenSource tokenSource = new();
CancelKeyPress += delegate { tokenSource.Cancel(); };

// Configure Inputs
ParserResult<ActionInputs> parser = Parser.Default.ParseArguments<ActionInputs>(args);
if (parser.Errors.ToArray() is { Length: > 0 } errors)
{
    foreach (CommandLine.Error error in errors)
    { WriteLine($"{nameof(error)}: {error.Tag}"); }
    Environment.Exit(2);
    return;
}
ActionInputs inputs = parser.Value;

// Find Local Files
DirectoryInfo workspace = new(inputs.Workspace);
DirectoryInfo output = workspace.CreateSubdirectory(inputs.Output);

// Authentication credentials
Configuration config = new()
{
    Username = inputs.Username,
    Password = inputs.Password,
    UserAgent = "kemocade/0.0.1 admin%40kemocade.com"
};

// Create instances of API's we'll need
AuthenticationApi authApi = new(config);
GroupsApi groupsApi = new(config);
TrackedGroup trackedGroup;

try
{
    // Log in
    WriteLine("Logging in...");
    CurrentUser currentUser = authApi.GetCurrentUser();

    if (currentUser == null)
    {
        WriteLine("2FA needed...");

        // Generate a 2fa code with the stored secret
        string key = inputs.Key.Replace(" ", string.Empty);
        Totp totp = new(Base32Encoding.ToBytes(key));

        // Make sure there's enough time left on the token
        int remainingSeconds = totp.RemainingSeconds();
        if (remainingSeconds < 5)
        {
            WriteLine("Waiting for new token...");
            await Task.Delay(TimeSpan.FromSeconds(remainingSeconds + 1));
        }

        WriteLine("Using 2FA code...");
        authApi.Verify2FA(new(totp.ComputeTotp()));
        currentUser = authApi.GetCurrentUser();

        if (currentUser == null)
        {
            WriteLine("Failed to validate 2FA");
            Environment.Exit(2);
        }
    }

    WriteLine($"Logged in as {currentUser.DisplayName}");

    // Get group
    string groupId = inputs.Group;
    Group group = groupsApi.GetGroup(groupId);
    int memberCount = group.MemberCount;
    WriteLine($"Got Group {group.Name}, Members: {memberCount}");

    // Get group roles
    WriteLine("Getting Group Roles...");
    GroupRole[] groupRoles = groupsApi
        .GetGroupRoles(groupId)
        .OrderBy(gr => gr.Name)
        .ToArray();
    WriteLine($"Got {groupRoles.Length} Group Roles");

    // Get group bans
    WriteLine("Getting Group Bans...");

    List<GroupMember> groupBansList = new();
    /*List<GroupMember> groupBansList = groupsApi.GetGroupBans(groupId);
    WriteLine(groupBansList == null);*/
    GroupMember[] groupBans = groupBansList
        .OrderBy(gm => gm.User.DisplayName)
        .ToArray();
    WriteLine($"Got {groupBans.Length} Group Bans");

    // Get group members
    WriteLine("Getting Group Members...");
    List<GroupMember> groupMembers = new();
    GroupMyMember me = group.MyMember;
    if (me != null)
    {
        groupMembers.Add
        (
            new
            (
                me.Id,
                me.GroupId,
                me.UserId,
                me.IsRepresenting,
                new (currentUser.Id, currentUser.DisplayName),
                me.RoleIds,
                me.JoinedAt,
                me.MembershipStatus,
                me.Visibility,
                me.IsSubscribedToAnnouncements
            )
        );
    }
    int addCount = 0;
    while (groupMembers.Count < memberCount)
    {
        List<GroupMember> added = groupsApi.GetGroupMembers(groupId, 100, addCount, 0);
        addCount += added.Count;
        groupMembers.AddRange(added);
        WriteLine(groupMembers.Count);
        await Task.Delay(1000);
    }
    groupMembers = groupMembers
        .OrderBy(gm => gm.User.DisplayName)
        .ToList();

    WriteLine($"Got {groupMembers.Count} Group Members");

    trackedGroup = new TrackedGroup
    {
        Roles = groupRoles
            .Select
            (
                gr =>
                new TrackedGroupRole
                {
                    Id = gr.Id,
                    Name = gr.Name,
                    MemberIndexes = groupMembers
                        .Select((gm, i) => (gm, i))
                        .Where(gmi => gmi.gm.RoleIds.Contains(gr.Id))
                        .Select(gmi => gmi.i)
                        .ToArray()
                }
            )
            .ToArray(),
        Bans = groupBans
            .Select
            (
                gb =>
                new TrackedGroupBan
                {
                    Id = gb.Id,
                    Name = gb.User.DisplayName
                }
            )
            .ToArray(),
        Members = groupMembers
            .Select
            (
                gm => new TrackedGroupMember
                {
                    Id = gm.UserId,
                    Name = gm.User.DisplayName,
                    RoleIndexes = groupRoles
                        .Select((gr, i) => (gr, i))
                        .Where(gri => gm.RoleIds.Contains(gri.gr.Id))
                        .Select(gri => gri.i)
                        .ToArray()
                }
            )
            .ToArray()
    };
}
catch (ApiException e)
{
    WriteLine("Exception when calling API: {0}", e.Message);
    WriteLine("Status Code: {0}", e.ErrorCode);
    WriteLine(e.ToString());
    Environment.Exit(2);
    return;
}

JsonSerializerOptions jsonSerializerOptions = new()
{ PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

string trackedGroupJson = Serialize(trackedGroup, jsonSerializerOptions);
WriteLine(trackedGroupJson);

FileInfo outputJson = new(Path.Join(output.FullName, "output.json"));
WriteAllText(outputJson.FullName, trackedGroupJson);

WriteLine("Done!");
Environment.Exit(0);