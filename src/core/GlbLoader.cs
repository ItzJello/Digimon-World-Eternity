using Godot;
using System;
using System.IO;

namespace DigimonWorldEternity;

public static class GlbLoader
{
    public static Node3D Load(string path, string name)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Missing {name} GLB", path);

        var document = new GltfDocument();
        var state = new GltfState();
        try
        {
            Error err = document.AppendFromFile(path, state);
            if (err != Error.Ok)
                throw new InvalidOperationException($"Could not read {name}: {err}");
            if (document.GenerateScene(state) is not Node3D scene)
                throw new InvalidOperationException($"{name} produced no 3D scene");
            scene.Name = name;
            return scene;
        }
        finally
        {
            document.Dispose();
            state.Dispose();
        }
    }
}
