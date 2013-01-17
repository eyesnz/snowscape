﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;

namespace Snowscape.TerrainGenerationViewer
{
    public class TerrainGenerationViewer : GameWindow
    {

        public class CloseEventArgs : EventArgs { }
        public delegate void CloseEventHandler(object source, CloseEventArgs e);
        public event CloseEventHandler OnClose;

        public TerrainGenerationViewer()
            : base(640, 480, new GraphicsMode(), "Snowscape", GameWindowFlags.Default, DisplayDevice.Default, 3, 0, GraphicsContextFlags.Default)
        {

        }

        protected override void OnClosed(EventArgs e)
        {
            OnClose(this, new CloseEventArgs());
            base.OnClosed(e);
        }


        protected override void OnLoad(EventArgs e)
        {
            this.VSync = VSyncMode.On;

            // create VBOs/Shaders etc

            // GL state
            GL.Enable(EnableCap.DepthTest);

            GL.MatrixMode(MatrixMode.Projection);
            GL.LoadIdentity();
            GL.Ortho(-1.0, 1.0, 1.0, -1.0, 0.001, 10.0);
            GL.MatrixMode(MatrixMode.Modelview);

            base.OnLoad(e);
        }

        protected override void OnResize(EventArgs e)
        {
            GL.Viewport(this.ClientRectangle);
            GL.MatrixMode(MatrixMode.Projection);
            GL.LoadIdentity();
            GL.Ortho(-1.0, 1.0, 1.0, -1.0, 0.001, 10.0);
            GL.MatrixMode(MatrixMode.Modelview);
            base.OnResize(e);
        }


        protected override void OnUpdateFrame(FrameEventArgs e)
        {
            //base.OnUpdateFrame(e);
        }

        protected override void OnRenderFrame(FrameEventArgs e)
        {
            GL.ClearColor(new Color4(0, 96, 64, 255));
            GL.ClearDepth(10.0);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            GL.MatrixMode(MatrixMode.Modelview);
            GL.PushMatrix();
            GL.LoadIdentity();
            GL.Translate(0.0, 0.0, -1.0);
            GL.Scale(0.5, 0.5, 0.5);

            //GL.Disable(EnableCap.Lighting);
            //GL.Disable(EnableCap.Texture2D);
            //GL.CullFace(CullFaceMode.Back);
            //GL.LineWidth(2.0f);

            //GL.Enable(EnableCap.Blend);
            //GL.BlendFunc(BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha);

            GL.Begin(BeginMode.TriangleStrip);

            GL.Color4(1.0f,0.5f,0.0f,0.5f);

            GL.Vertex3(-1.0f, -1.0f, 0.0f);
            GL.Vertex3(-1.0f, 1.0f, 0.0f);
            GL.Vertex3(1.0f, -1.0f, 0.0f);
            GL.Vertex3(1.0f, 1.0f, 0.0f);

            GL.End();

            GL.PopMatrix();

            SwapBuffers();
            //base.OnRenderFrame(e);
        }

    }
}
