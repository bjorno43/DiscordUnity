using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class DiscordUnitySmoke
{
    public static void Run()
    {
        int result = 1;
        try
        {
            var tests = Assembly.Load("DiscordUnityTests");
            var main = tests.GetType("DiscordUnityTests.Program").GetMethod("Main", BindingFlags.NonPublic | BindingFlags.Static);
            result = (int)main.Invoke(null, new object[] { new[] { "--network-smoke" } });
            Debug.Log("DiscordUnity Unity/Mono validation exit code: " + result);
        }
        catch (Exception exception) { Debug.LogException(exception); }
        EditorApplication.Exit(result);
    }
}
