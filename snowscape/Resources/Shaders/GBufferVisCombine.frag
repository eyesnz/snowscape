﻿#version 140
precision highp float;

uniform sampler2D posTex;
//uniform sampler2D normalTex;
uniform sampler2D paramTex;
uniform sampler2D heightTex;
uniform sampler2D shadeTex;

uniform vec4 boxparam;
uniform vec3 eyePos;
uniform vec3 sunVector;

in vec2 texcoord0;
out vec4 out_Colour;

// air absorbtion
vec3 Kr = vec3(0.18867780436772762, 0.4978442963618773, 0.6616065586417131);

mat3 m = mat3( 0.00,  0.80,  0.60,
              -0.80,  0.36, -0.48,
              -0.60, -0.48,  0.64 );

// credit: iq/rgba
float hash( float n )
{
    return fract(sin(n)*43758.5453);
}

// credit: iq/rgba
float noise( in vec3 x )
{
    vec3 p = floor(x);
    vec3 f = fract(x);

    f = f*f*(3.0-2.0*f);

    float n = p.x + p.y*57.0 + 113.0*p.z;

    float res = mix(mix(mix( hash(n+  0.0), hash(n+  1.0),f.x),
                        mix( hash(n+ 57.0), hash(n+ 58.0),f.x),f.y),
                    mix(mix( hash(n+113.0), hash(n+114.0),f.x),
                        mix( hash(n+170.0), hash(n+171.0),f.x),f.y),f.z);
    return res;
}

// credit: iq/rgba
float fbm( vec3 p )
{
    float f;
    f  = 0.5000*noise( p ); p = m*p*2.02;
    f += 0.2500*noise( p ); p = m*p*2.03;
    f += 0.1250*noise( p ); p = m*p*2.01;
    f += 0.0625*noise( p );
    return f;
}

float texel = 1.0 / boxparam.x;

float sampleHeight(vec2 posTile)
{
	return texture2D(heightTex,posTile * texel).r;
}

float sampleHeightNoise(vec2 posTile, float f, float a)
{
	return sampleHeight(posTile) + fbm(vec3(posTile.xy*f,0.0)) * a;
}

// pos in tile coords (0-boxparam.xy)
vec3 getNormal(vec2 pos)
{
    float h1 = sampleHeight(vec2(pos.x, pos.y - 0.5));
	float h2 = sampleHeight(vec2(pos.x, pos.y + 0.5));
    float h3 = sampleHeight(vec2(pos.x - 0.5, pos.y));
	float h4 = sampleHeight(vec2(pos.x + 0.5, pos.y));

    return normalize(vec3(h3-h4,1.0,h1-h2));
}

vec3 getNormalNoise(vec2 pos, float f, float a)
{
    float h1 = sampleHeightNoise(vec2(pos.x, pos.y - 0.5),f,a);
	float h2 = sampleHeightNoise(vec2(pos.x, pos.y + 0.5),f,a);
    float h3 = sampleHeightNoise(vec2(pos.x - 0.5, pos.y),f,a);
	float h4 = sampleHeightNoise(vec2(pos.x + 0.5, pos.y),f,a);

    return normalize(vec3(h3-h4,1.0,h1-h2));
}


// mie phase - @pyalot http://codeflow.org/entries/2011/apr/13/advanced-webgl-part-2-sky-rendering/
float phase(float alpha, float g){
	float gg = g*g;
    float a = 3.0*(1.0-gg);
    float b = 2.0*(2.0+gg);
    float c = 1.0+alpha*alpha;
    float d = pow(1.0+gg-2.0*g*alpha, 1.5);
    return (a/b)*(c/d);
}

float adepthTerrain(vec3 eye, vec3 target)
{
	return length(target - eye);
}

// atmospheric depth for sky ray - @pyalot http://codeflow.org/entries/2011/apr/13/advanced-webgl-part-2-sky-rendering/
// returns in the range 0..1 for eye inside atmosphere
float adepthSky(vec3 eye, vec3 dir)
{
    float a = dot(dir, dir);
    float b = 2.0*dot(dir, eye);
    float c = dot(eye, eye)-1.0;
    float det = b*b-4.0*a*c;
    float detSqrt = sqrt(det);
    float q = (-b - detSqrt)/2.0;
    float t1 = c/q;
    return t1;
}

// exponential absorbtion - @pyalot http://codeflow.org/entries/2011/apr/13/advanced-webgl-part-2-sky-rendering/
vec3 absorb(float dist, vec3 col, float f)
{
	return col - col * pow(Kr, vec3(f / dist));
}


vec3 getSkyColour(vec3 skyvector)
{
	vec3 skycol = 
		mix(
			vec3(0.02,0.03,0.2),
			vec3(0.4,0.6,0.9),
			pow(clamp(1.0-dot(skyvector,vec3(0.0,1.0,0.0)),0.0,1.0),2.0)
			);

	// scattering around the sun
	skycol += vec3(1.0,0.9,0.3) * pow(clamp(dot(skyvector,sunVector),0.0,1.0),300.0) * 4.0;

	// sun disk
	skycol += vec3(1.0,0.9,0.6) * smoothstep(0.9998,0.99995,dot(skyvector,sunVector)) * 8.0;

	return skycol;
}

float getShadowForGroundPos(vec3 p, float shadowHeight)
{
	return smoothstep(-2.0,-0.1,p.y - shadowHeight);
}

float getShadow(vec3 p)
{
	return smoothstep(-2.0,-0.1,p.y - texture2D(shadeTex,p.xz * texel).r);
}

float directIllumination(vec3 p, vec3 n, float shadowHeight)
{
	return  getShadowForGroundPos(p, shadowHeight) * clamp(dot(n,sunVector)+0.2,0,1);
}

// this is assumed to be constant across the entire terrain, because the terrian is small compared to the atmosphere
// this should be moved to a uniform
vec3 sunIntensity()
{
	return absorb(adepthSky(vec3(0.0), sunVector), vec3(1.0), 28.0);
}


vec3 terrainDiffuse(vec3 p, vec3 n, vec4 s, float shadowHeight)
{
	vec3 colH1 = pow(vec3(0.3,0.247,0.223),vec3(2.0));
	vec3 colL1 = pow(vec3(0.41,0.39,0.16),vec3(2.0));
	vec3 colW = pow(vec3(0.7,0.8,1.0),vec3(2.0));

	float looseblend = s.r*s.r;

	vec3 col = mix(colH1,colL1,looseblend);

	vec3 eyeDir = normalize(p-eyePos);
	vec3 wCol = vec3(0.1,0.2,0.25) + getSkyColour(reflect(eyeDir,n)) * smoothstep(-2.0,-0.1,p.y - shadowHeight);

	vec3 colW0 = wCol;  // blue water
	//vec4 colW0 = vec4(0.4,0.7,0.95,1.0);  // blue water
	vec3 colW1 = vec3(0.659,0.533,0.373);  // dirty water
	vec3 colW2 = vec3(1.2,1.3,1.4); // white water

	colW = mix(colW0,colW1,clamp(s.b*1.5,0,1));  // make water dirty->clean

	float waterblend = smoothstep(0.02,0.1,s.g) * 0.1 + 0.4 * s.g * s.g;

	col = mix(col,colW,waterblend); // water

    // misc vis
	vec3 colE = vec3(0.4,0.6,0.9);
	col += colE * clamp(s.a,0.0,1.0);

	return col;
}


vec3 generateCol(vec3 p, vec3 n, vec4 s, float shadowHeight, float AO)
{
	vec3 col = terrainDiffuse(p,n,s,shadowHeight);

	//float diffuse = directIllumination(p,n,shadowHeight);
	//col = col * diffuse + col * vec3(0.8,0.9,1.0) * 0.7 * AO;

	return 
		col * sunIntensity() * clamp(dot(n,sunVector)+0.2,0,1) * getShadowForGroundPos(p,shadowHeight) +
		col * vec3(0.8,0.9,1.0) * 0.7 * AO;

}






// get the amount of light scattered towards the eye when looking at target
// target is a terrain intersection
vec3 getInscatterTerrain(vec3 eye, vec3 target)
{
	vec3 p = eye;
	vec3 d = target-eye;
	float l = length(target-eye);
	vec3 c = vec3(0.0);

	for(float t=0.0;t<1.0;t+=0.05)
	{
		p = eye + d * t;

		// light / shadow factor
		float s = getShadow(p);

		c += vec3(0.4,0.6,0.9) * s * 0.0001;
	}
	
	return c * l;
}



void main(void)
{
	vec4 c = vec4(0.0,0.0,0.0,1.0);
	
	vec2 p = texcoord0.xy;
	vec4 posT = texture2D(posTex,p);
	float hitType = posT.a;
	vec4 pos = vec4(posT.xyz + eyePos,0.0);
	//vec4 normalT = texture2D(normalTex,p);
	vec4 paramT = texture2D(paramTex,p);
	//vec3 normal = normalize(normalT.xyz - 0.5);

	vec3 wpos = pos.xyz - eyePos;

	float smoothness = smoothstep(0.02,0.1,paramT.g)*8.0 + paramT.r*paramT.r * 2.0;
	
	vec3 normal = getNormalNoise(pos.xz,0.76,1.0 / (1.0+smoothness));
	//vec3 normal = getNormal(pos.xz);

	vec2 shadowAO = texture2D(shadeTex,pos.xz * texel).rg;

	float d = length(wpos);

	if (hitType > 0.6)
	{
	
		c.rgb = generateCol(pos.xyz,normal,paramT, shadowAO.r, shadowAO.g);	

		//c = vec4(0.0,0.0,0.0,1.0);
		//c.rgb += getInscatterTerrain(eyePos,pos.xyz);

		//vec4 fogcol = vec4(0.6,0.8,1.0,1.0);
		//c = mix(c,fogcol,getInscatterTerrain(eyePos,pos.xyz).r);

		//vec4 fogcol = vec4(0.6,0.8,1.0,1.0);
		//d /= 1024.0;
		//float fogamount = 1.0 / (exp(d * d * 0.2));
//
		//if (hitType < 0.5){
			//fogamount = 0.0;
		//}
//
		//c = mix(fogcol,c,fogamount);
		//
		//c.r = shadowAO.r;
		//c.g = shadowAO.g;
		//c.rgb = vec3(shadowAO.g * 0.4 + 0.9 * directIllumination(pos.xyz,normal, shadowAO.r));

		// visualize normal
		//c = vec4(normal*0.5+0.5,1.0);

		// visualize eye direction vector
		//c = vec4(normalize(pos.xyz - eyePos)*0.5+0.5,1.0);
	}
	else
	{
		if (hitType > 0.05)
		{

			//vec3 l = normalize(vec3(0.4,0.6,0.2));
			
			vec3 skycol = getSkyColour(normalize(posT.xyz));
			c = vec4(skycol,1.0);
			
			//vec4 skycol = mix(vec4(0.6,0.8,1.0,1.0),vec4(0.1,0.1,0.4,1.0),clamp(dot(posT.xyz,vec3(0.0,-1.0,0.0)),0.0,1.0));
			//c = mix(skycol,vec4(1.0),pow(clamp(dot(posT.xyz,-sunVector),0.0,1.0),50.0));

			// visualize eye direction vector
			//c = vec4(posT.xyz*0.5+0.5,1.0);
		}
		else
		{
			c = vec4(1.0,1.0,0.0,1.0);
		}
	}



	/*
	vec2 p = texcoord0.xy * 2.0;
	// split screen into 4
	if (p.x < 1.0)
	{
		if (p.y < 1.0)
		{
			vec3 pos = texture2D(posTex,p).xyz + eyePos;
			c.rgb = pos.xyz / 1024.0;
		}
		else
		{
			c = texture2D(normalTex,p-vec2(0.0,1.0));
		}
	}
	else
	{
		if (p.y < 1.0)
		{
			c = vec4(0.0);
		}
		else
		{
			c = texture2D(paramTex,p-vec2(1.0,1.0));
		}
	}
	*/

	// fog

	//out_Colour = vec4(c.rgb,1.0);
    out_Colour = vec4(sqrt(c.rgb),1.0);
	//out_Colour = vec4(pow(c.rgb,vec3(0.45)),1.0);
}
