using Godot;
using System.IO;

namespace DigimonWorldEternity;

/// <summary>
/// Renders each field once so the pieces can be looked at outside a bout.
/// Launch with the field_preview scene. Not part of the match flow.
/// </summary>
public partial class FieldPreview : Node3D
{
    public override async void _Ready()
    {
        StageLight.Add(this);
        var camera = new Camera3D { Current = true, Fov = 40f };
        AddChild(camera);
        string dir = "/tmp/fields";
        Directory.CreateDirectory(dir);
        foreach (ArenaMaps.Entry entry in ArenaMaps.All)
        {
            Node3D field = ArenaField.Build(entry.Stem);
            AddChild(field);
            ArenaField.ApplySky(this, entry.Stem);
            camera.GlobalPosition = new Vector3(-2.4f, 2.35f, 4.6f);
            camera.LookAt(new Vector3(2.2f, 0.7f, -1.4f), Vector3.Up);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Save(dir, entry.Stem + "_eye.png");
            camera.Fov = 44f;
            camera.GlobalPosition = new Vector3(0f, 22f, 0.1f);
            camera.LookAt(Vector3.Zero, Vector3.Up);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Save(dir, entry.Stem + "_top.png");
            camera.Fov = 40f;
            field.QueueFree();
        }
        GetTree().Quit();
    }

    private void Save(string dir, string name)
    {
        Image? image = GetViewport().GetTexture().GetImage();
        image?.SavePng(Path.Combine(dir, name));
        GD.Print($"preview {name}");
    }
}
