﻿using FreneticUtilities.FreneticDataSyntax;
using SwarmUI.Utils;

namespace SwarmUI.Accounts;

/// <summary>Represents a single user-role spec.</summary>
public class Role(string name)
{
    public class RoleData : AutoConfiguration
    {
        [ConfigComment("How many directories deep a user's custom OutPath can be.\nDefault is 5.")]
        public int MaxOutPathDepth = 5;

        [ConfigComment("What models are allowed, as a list of prefixes.\nFor example 'sdxl/' allows only models in the SDXL folder.\nOr, 'sdxl/,flux/' allows models in the SDXL or Flux folders.\nIf empty, no whitelist logic is applied.")]
        public HashSet<string> ModelWhitelist = [];

        [ConfigComment("What models are forbidden, as a list of prefixes.\nFor example 'sdxl/' forbids models in the SDXL folder.\nOr, 'sdxl/,flux/' forbids models in the SDXL or Flux folders.\nIf empty, no blacklist logic is applied.")]
        public HashSet<string> ModelBlacklist = [];

        [ConfigComment("Generic permission flags. '*' means all (admin).\nDefault is all.")]
        public HashSet<string> PermissionFlags = ["*"];

        [ConfigComment("How many images can try to be generating at the same time on this user.")]
        public int MaxT2ISimultaneous = 32;

        [ConfigComment("Whether the '.' symbol can be used in OutPath - if enabled, users may cause file system issues or perform folder escapes.")]
        public bool AllowUnsafeOutpaths = false;

        [ConfigComment("Human readable display name for this role.")]
        public string Name = "";

        [ConfigComment("Human readable description text about this role.")]
        public string Description = "";
    }

    /// <summary>Savable data for this role.</summary>
    public RoleData Data = new();

    /// <summary>Clean/simple ID for this role.</summary>
    public string ID = Utilities.StrictFilenameClean(name).Replace('/', '_');

    /// <summary>Whether this role is auto-generated (otherwise, the server own created this) (auto-generated roles may not be deleted).</summary>
    public bool IsAutoGenerated = false;

    /// <summary>Creates a role object with the combined values of all the given roles.</summary>
    public static Role Stack(IEnumerable<Role> roles)
    {
        RoleData role = new() { MaxOutPathDepth = 0, ModelWhitelist = [], ModelBlacklist = [], PermissionFlags = [], MaxT2ISimultaneous = 0, AllowUnsafeOutpaths = false, Name = "", Description = "" };
        foreach (RoleData otherRole in roles.Select(r => r.Data))
        {
            role.MaxOutPathDepth = Math.Max(role.MaxOutPathDepth, otherRole.MaxOutPathDepth);
            role.MaxT2ISimultaneous = Math.Max(role.MaxT2ISimultaneous, otherRole.MaxT2ISimultaneous);
            role.AllowUnsafeOutpaths = role.AllowUnsafeOutpaths || otherRole.AllowUnsafeOutpaths;
            role.PermissionFlags.UnionWith(otherRole.PermissionFlags);
            role.ModelWhitelist.UnionWith(otherRole.ModelWhitelist);
            role.ModelBlacklist.UnionWith(otherRole.ModelBlacklist);
        }
        return new Role("generated") { Data = role };
    }
}
