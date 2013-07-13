﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenTK;
using OpenTKExtensions;
using OpenTK.Graphics.OpenGL;
using Utils;
using Snowscape.TerrainRenderer.Mesh;

namespace Snowscape.TerrainRenderer.Renderers
{
    /// <summary>
    /// Renderer for a subset of a tile (ie: a patch)
    /// 
    /// This will use a mesh and a portion of a heightmap and associated textures.
    /// The mesh is designed to seamlessly tile to adjacent patches, assuming the source textures wrap around.
    /// </summary>
    public class GenerationVisPatchRenderer : ITileRenderer
    {
        private TerrainPatchMesh mesh;
        private ShaderProgram shader = new ShaderProgram("vistilepatch");

        public int Width { get; set; }
        public int Height { get; set; }

        public float Scale { get; set; }
        public Vector2 Offset { get; set; }

        public GenerationVisPatchRenderer(int width, int height, IPatchCache patchCache)
        {
            if (width != height)
            {
                throw new InvalidOperationException("Patch must be square.");
            }
            this.Width = width;
            this.Height = height;
            this.mesh = patchCache.GetPatchMesh(this.Width);
            this.Scale = 1.0f;
            this.Offset = Vector2.Zero;
        }

        public void Load()
        {
            InitShader();
        }

        private void InitShader()
        {
            // setup shader
            this.shader.Init(
                @"../../../Resources/Shaders/GenVisPatch.vert".Load(),
                @"../../../Resources/Shaders/GenVisPatch.frag".Load(),
                new List<Variable> 
                { 
                    new Variable(0, "vertex"), 
                    new Variable(1, "in_boxcoord") 
                },
                new string[]
                {
                    "out_Pos",
                    "out_Param",
                    "out_Normal"
                });
        }

        public void Render(TerrainTile tile, Matrix4 projection, Matrix4 view, Vector3 eyePos)
        {
            var boxparam = tile.GetBoxParam();

            GL.Enable(EnableCap.CullFace);
            GL.CullFace(CullFaceMode.Front);  // we only want to render back-faces

            tile.HeightTexture.Bind(TextureUnit.Texture0);
            tile.LinearSampler.Bind(TextureUnit.Texture0);
            tile.ParamTexture.Bind(TextureUnit.Texture1);

            this.shader
                .UseProgram()
                .SetUniform("projection_matrix", projection)
                .SetUniform("model_matrix", tile.ModelMatrix)
                .SetUniform("view_matrix", view)
                .SetUniform("heightTex", 0)
                .SetUniform("paramTex", 1)
                .SetUniform("eyePos", eyePos)
                .SetUniform("boxparam", boxparam)
                .SetUniform("patchSize", this.Width)
                .SetUniform("scale",this.Scale)
                .SetUniform("offset",this.Offset);
            this.mesh.Bind(this.shader.VariableLocation("vertex"), this.shader.VariableLocation("in_boxcoord"));
            this.mesh.Render();

            Sampler.Unbind(TextureUnit.Texture0);

        }



        public void Unload()
        {
            throw new NotImplementedException();
        }
    }
}