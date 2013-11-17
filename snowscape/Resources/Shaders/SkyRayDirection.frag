﻿#version 330
precision highp float;

uniform vec3 eyePos;
noperspective in vec4 eyeTarget;

out vec4 out_Normal;

void main(void)
{
	out_Normal = vec4(normalize(eyeTarget.xyz - eyePos)*0.5+0.5,0.1);
}
