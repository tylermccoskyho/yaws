﻿using Newtonsoft.Json.Linq;
using StableUI.Core;
using StableUI.Backends;
using StableUI.Utils;
using StableUI.Accounts;
using System.Runtime.ConstrainedExecution;
using StableUI.Text2Image;
using FreneticUtilities.FreneticExtensions;
using Microsoft.AspNetCore.Http;

namespace StableUI.WebAPI;

/// <summary>Internal helper for all the basic API routes.</summary>
public static class BasicAPIFeatures
{
    /// <summary>Called by <see cref="Program"/> to register the core API calls.</summary>
    public static void Register()
    {
        API.RegisterAPICall(GetNewSession);
        API.RegisterAPICall(GetMyUserData);
        API.RegisterAPICall(AddNewPreset);
        API.RegisterAPICall(DeletePreset);
        API.RegisterAPICall(GetCurrentStatus);
        API.RegisterAPICall(InterruptAll);
        T2IAPI.Register();
        BackendAPI.Register();
    }

    /// <summary>API Route to create a new session automatically.</summary>
    public static async Task<JObject> GetNewSession(HttpContext context)
    {
        return new JObject() { ["session_id"] = Program.Sessions.CreateAdminSession(context.Connection.RemoteIpAddress?.ToString() ?? "unknown").ID };
    }

    /// <summary>API Route to get the user's own base data.</summary>
    public static async Task<JObject> GetMyUserData(Session session)
    {
        return new JObject()
        {
            ["user_name"] = session.User.UserID,
            ["presets"] = new JArray(session.User.GetAllPresets().Select(p => p.NetData()).ToArray())
        };
    }

    /// <summary>API Route to add a new user parameters preset.</summary>
    public static async Task<JObject> AddNewPreset(Session session, string name, string description, JObject raw)
    {
        JObject paramData = (JObject)raw["data"];
        if (session.User.GetPreset(name) is not null && (paramData["is_edit"]?.ToString() ?? "") != "true")
        {
            return new JObject() { ["preset_fail"] = "A preset with that title already exists." };
        }
        T2IPreset preset = new()
        {
            Author = session.User.UserID,
            Title = name,
            Description = description,
            ParamMap = paramData.Properties().Select(p => (p.Name, p.Value.ToString())).PairsToDictionary(),
            PreviewImage = paramData.TryGetValue("image", out JToken imageVal) ? imageVal.ToString() : "imgs/model_placeholder.jpg"
        };
        if (preset.PreviewImage != "imgs/model_placeholder.jpg" && (!preset.PreviewImage.StartsWith("/Output") || preset.PreviewImage.Contains('?')))
        {
            Logs.Info($"User {session.User.UserID} tried to set a preset preview image to forbidden path: {preset.PreviewImage}");
            return new JObject() { ["preset_fail"] = "Forbidden preview-image path." };
        }
        session.User.SavePreset(preset);
        return new JObject() { ["success"] = true };
    }

    /// <summary>API Route to delete a user preset.</summary>
    public static async Task<JObject> DeletePreset(Session session, string preset)
    {
        return new JObject() { ["success"] = session.User.DeletePreset(preset) };
    }

    /// <summary>Gets current session status. Not an API call.</summary>
    public static JObject GetCurrentStatusRaw(Session session)
    {
        lock (session.StatsLocker)
        {
            return new JObject()
            {
                ["status"] = new JObject()
                {
                    ["waiting_gens"] = session.WaitingGenerations,
                    ["loading_models"] = session.LoadingModels,
                    ["waiting_backends"] = session.WaitingBackends,
                    ["live_gens"] = session.LiveGens
                }
            };
        }
    }

    /// <summary>API Route to get current waiting generation count, model loading count, etc.</summary>
    public static async Task<JObject> GetCurrentStatus(Session session)
    {
        return GetCurrentStatusRaw(session);
    }

    /// <summary>API Route to tell all waiting generations to interrupt.</summary>
    public static async Task<JObject> InterruptAll(Session session)
    {
        session.SessInterrupt.Cancel();
        session.SessInterrupt = new();
        return new JObject() { ["success"] = true };
    }
}
