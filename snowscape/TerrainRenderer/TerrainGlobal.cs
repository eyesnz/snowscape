﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenTKExtensions;
using OpenTK.Graphics.OpenGL;
using Utils;

namespace Snowscape.TerrainRenderer
{
    /// <summary>
    /// TerrainGlobal - stores global state of terrain
    /// 
    /// Knows about:
    /// - height map of entire terrain, either full scale or downsampled to fit within texture limits.
    /// - shadow height above each sample
    /// - amount of sky visible from each sample (for AO)
    /// - min / max height of terrain, to set exit conditions of rays
    /// </summary>
    public class TerrainGlobal
    {
        /// <summary>
        /// Height - single component float32
        /// </summary>
        public Texture HeightTexture { get; private set; }

        /// <summary>
        /// Shade - double-component float16
        /// R: Height of shadow plane.
        /// G: Proportion of sky visible.
        /// </summary>
        public Texture ShadeTexture { get; private set; }

        
        public float MinHeight { get; private set; }
        public float MaxHeight { get; private set; }

        public int Width { get; set; }
        public int Height { get; set; }

        public TerrainGlobal(int width, int height)
        {
            this.Width = width;
            this.Height = height;
        }

        public void Init()
        {
            // setup textures
            this.HeightTexture =
                new Texture(this.Width, this.Height, TextureTarget.Texture2D, PixelInternalFormat.R32f, PixelFormat.Red, PixelType.Float)
                .SetParameter(new TextureParameterInt(TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear))
                .SetParameter(new TextureParameterInt(TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear))
                .SetParameter(new TextureParameterInt(TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat))
                .SetParameter(new TextureParameterInt(TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat));

            this.ShadeTexture =
                new Texture(this.Width, this.Height, TextureTarget.Texture2D, PixelInternalFormat.Rg16f, PixelFormat.Rg, PixelType.HalfFloat)
                .SetParameter(new TextureParameterInt(TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear))
                .SetParameter(new TextureParameterInt(TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear))
                .SetParameter(new TextureParameterInt(TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat))
                .SetParameter(new TextureParameterInt(TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat));

            this.ShadeTexture.UploadEmpty();
        }

        public void SetDataFromTerrain(TerrainStorage.Terrain terrain)
        {
            if (terrain.Width != this.Width || terrain.Height != this.Height)
            {
                throw new InvalidOperationException("Terrain sizes do not match");
            }

            UploadHeightTextureFromTerrain(terrain);

        }

        private void UploadHeightTextureFromTerrain(TerrainStorage.Terrain terrain)
        {
            float[] height = new float[this.Width * this.Height];
            ParallelHelper.For2D(this.Width, this.Height, (x, y, i) =>
            {
                height[i] = terrain.Map[i].Height;
            });

            UploadHeightTexture(height);
        }

        private void UploadHeightTexture(float[] height)
        {
            this.MinHeight = height.Min();
            this.MaxHeight = height.Max();

            this.HeightTexture.Bind();
            this.HeightTexture.ApplyParameters();
            this.HeightTexture.Upload(height);
        }

    }
}