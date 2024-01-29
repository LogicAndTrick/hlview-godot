using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using Godot;
using Sledge.Formats.Bsp;
using Sledge.Formats.Bsp.Lumps;
using Sledge.Formats.Bsp.Objects;
using Sledge.Formats.Id;
using Sledge.Formats.Texture.Wad;
using Sledge.Formats.Texture.Wad.Lumps;
using static System.Net.Mime.MediaTypeNames;
using Color = Godot.Color;
using FileAccess = System.IO.FileAccess;
using Image = Godot.Image;
using Version = Sledge.Formats.Bsp.Version;

namespace HLView.Net;

public partial class BspNode : Node3D
{
    private Shader _shader;

    private readonly System.Collections.Generic.Dictionary<int, Texture2D> _textures = new();

    public override void _Ready()
	{
        _shader = GD.Load<Shader>("res://bsp_shader.gdshader");
    }

	public override void _Process(double delta)
	{
		//
	}

    [Signal]
    public delegate void LoadCompletedEventHandler();

    public void Clear()
    {
        foreach (var child in GetChildren().ToList()) RemoveChild(child);
        _textures.Clear();
    }

    public void LoadFile(string gameDir, string fileName)
    {
        GD.Print(fileName);
        Clear();

        using var stream = File.Open(fileName, FileMode.Open, FileAccess.Read, FileShare.Read);

        var bsp = new BspFile(stream);

        var worldspawn = bsp.Entities.FirstOrDefault(x => x.ClassName == "worldspawn");
        var wadKey = worldspawn?.Get<string>("wad", null) ?? "";
        var wads = new List<WadFile>();
        foreach (var wadPath in wadKey.Split(';'))
        {
            var wadFileName = Path.Join(gameDir, Path.GetFileName(wadPath));
            if (!File.Exists(wadFileName)) continue;

            using var wadStream = File.OpenRead(wadFileName);
            wads.Add(new WadFile(wadStream));
        }

        GenerateTexturesForBsp(bsp, wads);

        {
            var model = bsp.Models[0];
            var meshes = GenerateGeometryForModel(model, bsp);
            foreach (var mesh in meshes)
            {
                var mi3 = new MeshInstance3D { Mesh = mesh };
                AddChild(mi3);
            }
        }

        foreach (var ent in bsp.Entities.Where(x => x.Model > 0))
        {
            var model = bsp.Models[ent.Model];
            var meshes = GenerateGeometryForModel(model, bsp);
            foreach (var mesh in meshes)
            {
                var mi3 = new MeshInstance3D { Mesh = mesh };
                AddChild(mi3);
                mi3.Rotation = ent.GetVector3("angles", System.Numerics.Vector3.Zero).ToGodotVector3();
                mi3.Position = ent.GetVector3("origin", System.Numerics.Vector3.Zero).ToGodotVector3();
            }
        }

        EmitSignal(SignalName.LoadCompleted);
    }

    private void GenerateTexturesForBsp(BspFile bsp, List<WadFile> wadFiles)
    {
        for (var i = 0; i < bsp.Textures.Count; i++)
        {
            var tex = bsp.Textures[i];
            if (tex.NumMips == 0)
            {
                // try loading an external image
                foreach (var wadFile in wadFiles)
                {
                    var extTex = wadFile.Entries.FirstOrDefault(x => string.Equals(x.Name, tex.Name, StringComparison.InvariantCultureIgnoreCase));
                    if (extTex != null)
                    {
                        var lump = wadFile.GetLump(extTex);
                        if (lump is RawTextureLump rtl)
                        {
                            tex = rtl;
                            break;
                        }
                    }
                }
            }

            if (tex.NumMips == 0) continue;

            var mip = tex.MipData[0];
            var convertedData = new byte[mip.Length * 4];
            System.Array.Fill(convertedData, byte.MaxValue);
            for (var j = 0; j < mip.Length; j++)
            {
                System.Array.Copy(tex.Palette, mip[j] * 3, convertedData, j * 4, 3);
            }

            var image = Image.CreateFromData((int) tex.Width, (int)tex.Height, false, Image.Format.Rgba8, convertedData);
            image.GenerateMipmaps();
            var texture = ImageTexture.CreateFromImage(image);
            _textures.Add(i, texture);
        }
    }

    private List<ArrayMesh> GenerateGeometryForModel(Model model, BspFile bsp)
    {
        var faces = new List<Face>();
        for (var i = model.FirstFace; i < model.FirstFace + model.NumFaces; i++)
        {
            var face = bsp.Faces[i];
            faces.Add(face);
        }
        faces.Sort((a, b) =>
        {
            var ta = bsp.Texinfo[a.TextureInfo];
            var tb = bsp.Texinfo[b.TextureInfo];
            return ta.MipTexture.CompareTo(tb.MipTexture);
        });

        var mesh = new ArrayMesh();
        var meshes = new List<ArrayMesh> { mesh };

        foreach (var faceGroup in faces.GroupBy(x => bsp.Texinfo[x.TextureInfo].MipTexture))
        {
            // create a new mesh if we have too many
            if (mesh.GetSurfaceCount() > 100)
            {
                mesh = new ArrayMesh();
                meshes.Add(mesh);
            }

            var mipTexture = bsp.Textures[faceGroup.Key];
            var mipTextureSize = new Vector2(mipTexture.Width, mipTexture.Height);
            var builder = new LightmapBuilder();

            var mappedFaces = new List<LightmappedFace>();

            // calculate the base data
            foreach (var face in faceGroup)
            {
                var mappedFace = new LightmappedFace();
                mappedFaces.Add(mappedFace);

                var plane = bsp.Planes[face.Plane];
                var texInfo = bsp.Texinfo[face.TextureInfo];

                // map the vertices
                for (var j = face.FirstEdge; j < face.FirstEdge + face.NumEdges; j++)
                {
                    var edgeIndex = bsp.Surfedges[j];
                    var edge = bsp.Edges[Math.Abs(edgeIndex)];

                    var vertexIndex = edgeIndex > 0 ? edge.Start : edge.End;
                    var vertex = bsp.Vertices[vertexIndex].ToGodotVector3();

                    var normal = plane.Normal.ToGodotVector3();

                    var sn = texInfo.S.ToGodotVector3();
                    var u = vertex.Dot(sn) + texInfo.S.W;

                    var tn = texInfo.T.ToGodotVector3();
                    var v = vertex.Dot(tn) + texInfo.T.W;

                    // todo lightmaps
                    mappedFace.Vertices.Add(new LightmappedVertex
                    {
                        Position = vertex,
                        Normal = normal,
                        Texture = new Vector2(u, v), // needs to be divided by the texture dimensions
                        Color = Colors.White,
                        Lightmap = default
                    });
                }

                var minu = mappedFace.Vertices.Min(x => x.Texture.X);
                var maxu = mappedFace.Vertices.Max(x => x.Texture.X);
                var minv = mappedFace.Vertices.Min(x => x.Texture.Y);
                var maxv = mappedFace.Vertices.Max(x => x.Texture.Y);

                var extentH = maxu - minu;
                var extentV = maxv - minv;

                var mapWidth = (int)Math.Ceiling(maxu / 16) - (int)Math.Floor(minu / 16) + 1;
                var mapHeight = (int)Math.Ceiling(maxv / 16) - (int)Math.Floor(minv / 16) + 1;

                if (face.LightmapOffset < 0 || face.LightmapOffset >= bsp.Lightmaps.RawLightmapData.Length || face.Styles[0] == byte.MaxValue)
                {
                    // fullbright
                    mappedFace.LightmapAllocation = builder.FullbrightRectangle;
                }
                else
                {
                    if (bsp.Version == Version.Quake1)
                    {
                        // convert 8 bit to 24 bit
                        const int bytesPerPixel = 3;
                        var data = new byte[bytesPerPixel * mapWidth * mapHeight];
                        for (var i = 0; i < mapWidth * mapHeight; i++)
                        {
                            data[i * bytesPerPixel + 0] = bsp.Lightmaps.RawLightmapData[face.LightmapOffset + i];
                            data[i * bytesPerPixel + 1] = bsp.Lightmaps.RawLightmapData[face.LightmapOffset + i];
                            data[i * bytesPerPixel + 2] = bsp.Lightmaps.RawLightmapData[face.LightmapOffset + i];
                        }
                        mappedFace.LightmapAllocation = builder.Allocate(mapWidth, mapHeight, data, 0);
                    }
                    else
                    {
                        mappedFace.LightmapAllocation = builder.Allocate(mapWidth, mapHeight, bsp.Lightmaps.RawLightmapData, face.LightmapOffset);
                    }

                    foreach (var v in mappedFace.Vertices)
                    {
                        v.Lightmap.X = (v.Texture.X - minu) / extentH;
                        v.Lightmap.Y = (v.Texture.Y - minv) / extentV;
                    }
                }
            }

            // recalculate based on texture and lightmap atlas data
            var lightmapWidth = builder.Width;
            var lightmapHeight = builder.Height;
            foreach (var mappedFace in mappedFaces)
            {
                var rect = mappedFace.LightmapAllocation;
                foreach (var vertex in mappedFace.Vertices)
                {
                    // Lightmap X/Y are currently mapped to local lightmap space, need to remap to texture space
                    vertex.Lightmap.X = (rect.X + 0.5f) / lightmapWidth + (vertex.Lightmap.X * (rect.Width - 1)) / lightmapWidth;
                    vertex.Lightmap.Y = (rect.Y + 0.5f) / lightmapHeight + (vertex.Lightmap.Y * (rect.Height - 1)) / lightmapHeight;
                    vertex.Texture /= mipTextureSize;
                }
            }

            // create the lightmap texture
            var lightmapImage = Image.CreateFromData(lightmapWidth, lightmapHeight, false, Image.Format.Rgb8, builder.Data);
            lightmapImage.GenerateMipmaps();
            var lightmapTexture = ImageTexture.CreateFromImage(lightmapImage);

            var surfaceTool = new SurfaceTool();
            surfaceTool.Begin(Mesh.PrimitiveType.Triangles);

            // create the material
            var material = new ShaderMaterial
            {
                Shader = _shader
            };
            material.SetShaderParameter("albedo_texture", _textures[faceGroup.Key]);
            material.SetShaderParameter("lightmap_texture", lightmapTexture);
            surfaceTool.SetMaterial(material);

            // triangulate the faces
            foreach (var face in mappedFaces)
            {
                for (var j = 1; j < face.Vertices.Count - 1; j++)
                {
                    var a = face.Vertices[0];
                    var c = face.Vertices[j + 1];
                    var b = face.Vertices[j];

                    surfaceTool.SetUV(a.Texture);
                    surfaceTool.SetUV2(a.Lightmap);
                    surfaceTool.SetNormal(a.Normal);
                    surfaceTool.SetColor(a.Color);
                    surfaceTool.AddVertex(a.Position);

                    surfaceTool.SetUV(b.Texture);
                    surfaceTool.SetUV2(b.Lightmap);
                    surfaceTool.SetNormal(b.Normal);
                    surfaceTool.SetColor(b.Color);
                    surfaceTool.AddVertex(b.Position);

                    surfaceTool.SetUV(c.Texture);
                    surfaceTool.SetUV2(c.Lightmap);
                    surfaceTool.SetNormal(c.Normal);
                    surfaceTool.SetColor(c.Color);
                    surfaceTool.AddVertex(c.Position);
                }
            }

            surfaceTool.Commit(mesh);
        }

        return meshes;
    }

    private class LightmappedFace
    {
        public List<LightmappedVertex> Vertices { get; } = new();
        public Rectangle LightmapAllocation { get; set; }
    }

    private class LightmappedVertex
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector2 Texture;
        public Color Color = Colors.White;
        public Vector2 Lightmap;
    }
}
