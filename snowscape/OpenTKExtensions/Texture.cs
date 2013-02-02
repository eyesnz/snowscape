﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;
using NLog;

namespace OpenTKExtensions
{
    public class Texture
    {
        private static Logger log = LogManager.GetCurrentClassLogger();

        public int ID { get; private set; }
        public TextureTarget Target { get; private set; }
        public PixelInternalFormat InternalFormat { get; private set; }
        public PixelFormat Format { get; set; }
        public PixelType Type { get; set; }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public string Name { get; set; }

        private List<ITextureParameter> parameters = new List<ITextureParameter>();
        public List<ITextureParameter> Parameters { get { return this.parameters; } }

        public Texture(int width, int height, TextureTarget target, PixelInternalFormat internalFormat, PixelFormat format, PixelType type)
        {
            this.ID = -1;
            this.Target = target;
            this.InternalFormat = internalFormat;
            this.Width = width;
            this.Height = height;
            this.Format = format;
            this.Type = type;
        }

        public Texture SetParameter(ITextureParameter param)
        {
            this.Parameters.Add(param);
            return this;
        }

        public int Init()
        {
            if (this.ID == -1)
            {
                this.ID = GL.GenTexture();
                log.Trace("Texture.GenerateID returned {0}", this.ID);
            }
            return this.ID;
        }

        public void ApplyParameters()
        {
            foreach (var param in this.Parameters)
            {
                param.Apply(this.Target);
            }
        }

        public void Bind()
        {
            if (Init() != -1)
            {
                GL.BindTexture(this.Target, this.ID);
            }
        }

        public void Bind(TextureUnit unit)
        {
            GL.ActiveTexture(unit);
            this.Bind();
        }

        public void Upload<T>(T[] data) where T : struct
        {
            if (Init() != -1)
            {
                this.Bind();
                this.ApplyParameters();
                this.UploadImage(data);
            }
        }

        private void UploadImage<T>(T[] data) where T : struct
        {
            GL.TexImage2D<T>(this.Target, 0, this.InternalFormat, this.Width, this.Height, 0, this.Format, this.Type, data);
        }

        public void RefreshImage(IntPtr data)
        {
            this.Bind();
            GL.TexSubImage2D(this.Target, 0, 0, 0, this.Width, this.Height, this.Format, this.Type, data);
        }

        public void Unload()
        {
            if (this.ID != -1)
            {
                GL.DeleteTexture(this.ID);
            }
        }
    }
}
