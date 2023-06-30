﻿using FreneticUtilities.FreneticExtensions;
using FreneticUtilities.FreneticToolkit;
using Newtonsoft.Json.Linq;
using StableUI.Accounts;
using StableUI.Core;
using StableUI.DataHolders;
using StableUI.Text2Image;
using StableUI.Utils;
using StableUI.WebAPI;
using System.IO;
using System.Net.WebSockets;

namespace StableUI.Builtin_GridGeneratorExtension;

/// <summary>Extension that adds a tool to generate grids of images.</summary>
public class GridGeneratorExtension : Extension
{
    public override void OnPreInit()
    {
        ScriptFiles.Add("Assets/grid_gen.js");
        StyleSheetFiles.Add("Assets/grid_gen.css");
        GridGenCore.ASSETS_DIR = $"{FilePath}/Assets";
        GridGenCore.EXTRA_FOOTER = $"Images area auto-generated by an AI (Stable Diffusion) and so may not have been reviewed by the page author before publishing.\n<script src=\"stableui_gridgen_local.js?vary={Utilities.VaryID}\"></script>";
        GridGenCore.EXTRA_ASSETS.Add("stableui_gridgen_local.js");
        T2IParamTypes.Register(new("[Grid Gen] Prompt Replace", "Replace text in the prompt (or negative prompt) with some other text.",
            T2IParamDataType.TEXT, "", (s, p) => throw new Exception("Prompt replace mishandled!"), VisibleNormally: false, Toggleable: true, ParseList: (list) =>
            {
                if (list.Any(v => v.Contains('=')))
                {
                    return list;
                }
                string first = list[0];
                return list.Select(v => $"{first}={v}").ToList();
            }));
        T2IParamTypes.Register(new("[Grid Gen] Presets", "Apply parameter presets to the image. Can use a comma-separated list to apply multiple per-cell, eg 'a, b || a, c || b, c'",
            T2IParamDataType.TEXT, "", (s, p) =>
            {
                foreach (T2IPreset preset in s.SplitFast(',').Select(s => p.SourceSession.User.GetPreset(s.Trim())).Where(p => p is not null))
                {
                    preset.ApplyTo(p);
                }
            }, VisibleNormally: false, Toggleable: true, ValidateValues: false, GetValues: (session) => session.User.GetAllPresets().Select(p => p.Title).ToList()));
        GridGenCore.GridCallInitHook = (call) =>
        {
            call.LocalData = new GridCallData();
        };
        GridGenCore.GridCallParamAddHook = (call, param, val) =>
        {
            if (call.Grid.MinWidth == 0)
            {
                call.Grid.MinWidth = call.Grid.InitialParams.Width;
            }
            if (call.Grid.MinHeight == 0)
            {
                call.Grid.MinHeight = call.Grid.InitialParams.Height;
            }
            string cleaned = T2IParamTypes.CleanTypeName(param);
            if (cleaned == "gridgenpromptreplace")
            {
                (call.LocalData as GridCallData).Replacements.Add(val);
                return true;
            }
            else if (cleaned == "width" || cleaned == "outwidth")
            {
                call.Grid.MinWidth = Math.Min(call.Grid.MinWidth, int.Parse(val));
            }
            else if (cleaned == "height" || cleaned == "outheight")
            {
                call.Grid.MinHeight = Math.Min(call.Grid.MinHeight, int.Parse(val));
            }
            return false;
        };
        GridGenCore.GridCallApplyHook = (call, param, dry) =>
        {
            foreach (string replacement in (call.LocalData as GridCallData).Replacements)
            {
                string[] parts = replacement.Split('=', 2);
                string key = parts[0].Trim();
                string val = parts[1].Trim();
                param.Prompt = param.Prompt.Replace(key, val);
                param.NegativePrompt = param.NegativePrompt.Replace(key, val);
                foreach (string paramId in param.OtherParams.Keys.Where(k => k.EndsWith("_prompt") && param.OtherParams[k] is string).ToArray())
                {
                    param.OtherParams[paramId] = param.OtherParams[paramId].ToString().Replace(key, val);
                }
            }
        };
        GridGenCore.GridRunnerPreRunHook = (runner) =>
        {
            // TODO: Progress update
        };
        GridGenCore.GridRunnerPreDryHook = (runner) =>
        {
            // Nothing to do.
        };
        GridGenCore.GridRunnerPostDryHook = (runner, param, set) =>
        {
            if (param.Seed == -1)
            {
                param.Seed = Random.Shared.Next();
            }
            if (param.VarSeed == -1)
            {
                param.VarSeed = Random.Shared.Next();
            }
            StableUIGridData data = runner.Grid.LocalData as StableUIGridData;
            if (data.Claim.ShouldCancel)
            {
                Logs.Debug("Grid gen hook cancelling per user interrupt request.");
                runner.Grid.MustCancel = true;
                return Task.CompletedTask;
            }
            Task[] waitOn = data.GetActive();
            if (waitOn.Length > data.Session.User.Settings.MaxT2ISimultaneous)
            {
                Task.WaitAny(waitOn);
            }
            if (Volatile.Read(ref data.ErrorOut) is not null)
            {
                throw new InvalidOperationException("Errored");
            }
            void setError(string message)
            {
                Logs.Error($"Grid generator hit error: {message}");
                Volatile.Write(ref data.ErrorOut, new JObject() { ["error"] = message });
                data.Signal.Set();
            }
            T2IParams thisParams = param.Clone();
            Task t = Task.Run(() => T2IAPI.CreateImageTask(thisParams, data.Claim, data.AddOutput, setError, true, 10, // TODO: Max timespan configurable
                (outputs) =>
                {
                    if (outputs.Length != 1)
                    {
                        setError($"Server generated {outputs.Length} images when only expecting 1.");
                        return;
                    }
                    string targetPath = $"{set.Grid.Runner.BasePath}/{set.BaseFilepath}.{set.Grid.Format}";
                    string dir = targetPath.Replace('\\', '/').BeforeLast('/');
                    if (!Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                    File.WriteAllBytes(targetPath, outputs[0].ImageData);
                    data.AddOutput(new JObject() { ["image"] = $"/{set.Grid.Runner.URLBase}/{set.BaseFilepath}.{set.Grid.Format}", ["metadata"] = outputs[0].GetMetadata() });
                }));
            lock (data.UpdateLock)
            {
                data.Rendering.Add(t);
            }
            return t;
        };
        GridGenCore.PostPreprocessCallback = (grid) =>
        {
            StableUIGridData data = grid.Grid.LocalData as StableUIGridData;
            data.Claim.Extend(grid.TotalRun, 0, 0, 0);
            data.AddOutput(BasicAPIFeatures.GetCurrentStatusRaw(data.Session));
        };
    }

    public override void OnInit()
    {
        API.RegisterAPICall(GridGenRun);
    }

    public class GridCallData
    {
        public List<string> Replacements = new();
    }

    public class StableUIGridData
    {
        public List<Task> Rendering = new();

        public LockObject UpdateLock = new();

        public ConcurrentQueue<JObject> Generated = new();

        public Session Session;

        public Session.GenClaim Claim;

        public JObject ErrorOut;

        public AsyncAutoResetEvent Signal = new(false);

        public Task[] GetActive()
        {
            lock (UpdateLock)
            {
                return Rendering.Where(x => !x.IsCompleted).ToArray();
            }
        }
        public void AddOutput(JObject obj)
        {
            Generated.Append(obj);
            Signal.Set();
        }
    }

    public static JObject ExToError(Exception ex)
    {
        if (ex is AggregateException && ex.InnerException is AggregateException)
        {
            ex = ex.InnerException;
        }
        if (ex is AggregateException && ex.InnerException is InvalidDataException)
        {
            ex = ex.InnerException;
        }
        if (ex is InvalidDataException)
        {
            return new JObject() { ["error"] = $"Failed due to: {ex.Message}" };
        }
        else
        {
            Logs.Error($"Grid Generator hit error: {ex}");
            return new JObject() { ["error"] = "Failed due to internal error." };
        }
    }

    public async Task<JObject> GridGenRun(WebSocket socket, Session session, JObject raw, string outputFolderName, bool doOverwrite, bool fastSkip, bool generatePage, bool publishGenMetadata, bool dryRun)
    {
        using Session.GenClaim claim = session.Claim(gens: 1);
        T2IParams baseParams = new(session) { BatchID = Random.Shared.Next(int.MaxValue) };
        try
        {
            foreach ((string key, JToken val) in (raw["baseParams"] as JObject))
            {
                if (T2IParamTypes.Types.ContainsKey(T2IParamTypes.CleanTypeName(key)))
                {
                    T2IParamTypes.ApplyParameter(key, val.ToString(), baseParams);
                }
            }
        }
        catch (InvalidDataException ex)
        {
            await socket.SendJson(new JObject() { ["error"] = ex.Message }, API.WebsocketTimeout);
            return null;
        }
        if (baseParams.Seed == -1)
        {
            baseParams.Seed = Random.Shared.Next();
        }
        if (baseParams.VarSeed == -1)
        {
            baseParams.VarSeed = Random.Shared.Next();
        }
        async Task sendStatus()
        {
            await socket.SendJson(BasicAPIFeatures.GetCurrentStatusRaw(session), API.WebsocketTimeout);
        }
        await sendStatus();
        outputFolderName = Utilities.FilePathForbidden.TrimToNonMatches(outputFolderName);
        if (outputFolderName.Contains('.'))
        {
            await socket.SendJson(new JObject() { ["error"] = "Output folder name cannot contain dots." }, API.WebsocketTimeout);
            return null;
        }
        if (outputFolderName.Trim() == "")
        {
            await socket.SendJson(new JObject() { ["error"] = "Output folder name cannot be empty." }, API.WebsocketTimeout);
            return null;
        }
        StableUIGridData data = new() { Session = session, Claim = claim };
        try
        {
            Task mainRun = Task.Run(() => GridGenCore.Run(baseParams, raw["gridAxes"], data, null, session.User.OutputDirectory, "Output", outputFolderName, doOverwrite, fastSkip, generatePage, publishGenMetadata, dryRun));
            while (!mainRun.IsCompleted || data.GetActive().Any() || data.Generated.Any())
            {
                await data.Signal.WaitAsync(TimeSpan.FromSeconds(1));
                Program.GlobalProgramCancel.ThrowIfCancellationRequested();
                while (data.Generated.TryDequeue(out JObject toSend))
                {
                    await socket.SendJson(toSend, API.WebsocketTimeout);
                }
            }
            if (mainRun.IsFaulted)
            {
                throw mainRun.Exception;
            }
        }
        catch (Exception ex)
        {
            if (Volatile.Read(ref data.ErrorOut) is null)
            {
                Volatile.Write(ref data.ErrorOut, ExToError(ex));
            }
        }
        Task faulted = data.Rendering.FirstOrDefault(t => t.IsFaulted);
        JObject err = Volatile.Read(ref data.ErrorOut);
        if (faulted is not null && err is null)
        {
            err = ExToError(faulted.Exception);
        }
        if (err is not null)
        {
            Logs.Error($"GridGen stopped while running: {err}");
            await socket.SendJson(err, TimeSpan.FromMinutes(1));
            return null;
        }
        Logs.Info("Grid Generator completed successfully");
        string lastJsFile = $"{session.User.OutputDirectory}/{outputFolderName}/last.js";
        if (File.Exists(lastJsFile))
        {
            File.Delete(lastJsFile);
        }
        claim.Complete(gens: 1);
        await sendStatus();
        await socket.SendJson(new JObject() { ["success"] = "complete" }, API.WebsocketTimeout);
        return null;
    }
}
