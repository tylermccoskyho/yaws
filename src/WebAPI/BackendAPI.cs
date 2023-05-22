﻿using FreneticUtilities.FreneticDataSyntax;
using FreneticUtilities.FreneticExtensions;
using Newtonsoft.Json.Linq;
using StableUI.Backends;
using StableUI.Core;
using StableUI.Utils;

namespace StableUI.WebAPI;

public class BackendAPI
{
    public static void Register()
    {
        API.RegisterAPICall(ListBackendTypes);
        API.RegisterAPICall(ListBackends);
        API.RegisterAPICall(DeleteBackend);
        API.RegisterAPICall(EditBackend);
        API.RegisterAPICall(AddNewBackend);
    }

    /// <summary>API route to list currently available backend-types.</summary>
    public static async Task<JObject> ListBackendTypes()
    {
        return new() { ["list"] = JToken.FromObject(Program.Backends.BackendTypes.Values.Select(b => b.NetDescription).ToList()) };
    }

    /// <summary>Create a network object to represent a backend cleanly.</summary>
    public static JObject BackendToNet(BackendHandler.T2IBackendData backend)
    {
        return new JObject()
        {
            ["type"] = backend.Backend.HandlerTypeData.ID,
            ["status"] = backend.Backend.Status.ToString().ToLowerFast(),
            ["id"] = backend.ID,
            ["settings"] = JToken.FromObject(backend.Backend.InternalSettingsAccess.Save(true).ToSimple()),
            ["modcount"] = backend.ModCount
        };
    }

    /// <summary>API route to shutdown and delete a registered backend.</summary>
    public static async Task<JObject> DeleteBackend(int backend_id)
    {
        if (Program.LockSettings)
        {
            return new() { ["error"] = "Settings are locked." };
        }
        if (await Program.Backends.DeleteById(backend_id))
        {
            return new JObject() { ["result"] = "Deleted." };
        }
        return new JObject() { ["result"] = "Already didn't exist." };
    }

    /// <summary>API route to modify and re-init an already registered backend.</summary>
    public static async Task<JObject> EditBackend(int backend_id, JObject raw_inp)
    {
        if (Program.LockSettings)
        {
            return new() { ["error"] = "Settings are locked." };
        }
        if (!raw_inp.TryGetValue("settings", out JToken jval) || jval is not JObject settings)
        {
            return new() { ["error"] = "Missing settings." };
        }
        FDSSection parsed = FDSSection.FromSimple(settings.ToBasicObject());
        BackendHandler.T2IBackendData result = await Program.Backends.EditById(backend_id, parsed);
        if (result is null)
        {
            return new() { ["error"] = $"Invalid backend ID {backend_id}" };
        }
        return BackendToNet(result);
    }

    /// <summary>API route to list currently registered backends.</summary>
    public static async Task<JObject> ListBackends()
    {
        JObject toRet = new();
        foreach (BackendHandler.T2IBackendData data in Program.Backends.T2IBackends.Values.OrderBy(d => d.ID))
        {
            toRet[data.ID.ToString()] = BackendToNet(data);
        }
        return toRet;
    }

    /// <summary>API route to add a new backend.</summary>
    public static async Task<JObject> AddNewBackend(string type_id)
    {
        if (Program.LockSettings)
        {
            return new() { ["error"] = "Settings are locked." };
        }
        if (!Program.Backends.BackendTypes.TryGetValue(type_id, out BackendHandler.BackendType type))
        {
            return new() { ["error"] = $"Invalid backend type: {type_id}" };
        }
        BackendHandler.T2IBackendData data = Program.Backends.AddNewOfType(type);
        return BackendToNet(data);
    }
}
