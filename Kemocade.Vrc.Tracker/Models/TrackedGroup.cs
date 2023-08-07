namespace Kemocade.Vrc.Tracker.Models;

internal record TrackedGroup
{
    public required TrackedGroupRole[] Roles { get; init; }

    public required TrackedGroupMember[] Members { get; init; }

    internal record TrackedGroupRole
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required int[] MemberIndexes { get; init; }
    }

    internal record TrackedGroupMember
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required int[] RoleIndexes { get; init; }
    }
}