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
    public class Shader
    {
        private static Logger log = LogManager.GetCurrentClassLogger();

        public int Handle { get; set; }
        public string Source { get; set; }
        public ShaderType Type { get; set; }

        public Shader()
        {
            this.Type = ShaderType.FragmentShader;
            this.Handle = -1;
            this.Source = string.Empty;
        }

        public void Create()
        {
            if (this.Handle != -1)
            {
                throw new InvalidOperationException("Shader already created");
            }
            this.Handle = GL.CreateShader(this.Type);
        }

        public void Compile()
        {
            if (this.Handle < 0)
            {
                throw new InvalidOperationException("Shader not created");
            }
            GL.ShaderSource(this.Handle, this.Source);
            GL.CompileShader(this.Handle);
            log.Trace("Shader.Compile: {0}",GL.GetShaderInfoLog(this.Handle));
        }





    }
}