﻿#include "helpers/lighting.hlsl"
#include "helpers/pbr.hlsl"

struct PSInput {
   float3 positionObject : POSITION1;
   float3 positionWorld : POSITION2;
   float4 position : SV_Position;
   float3 normalObject : NORMAL1;
   float3 normalWorld : NORMAL2;
   float4 color : COLOR;
   float2 uv : TEXCOORD;
};

PSInput VSMain(
   float3 position : POSITION,
   float3 normal : NORMAL,
   float4 color : COLOR,
   float2 uv : TEXCOORD,
   float4x4 world : INSTANCE_TRANSFORM
) {
   PSInput result;

   float4x4 batchWorld = mul(batchTransform, world);
   float4 positionWorld = mul(batchWorld, float4(position, 1));
   float4 normalWorld = mul(batchWorld, float4(normal, 0));
   
   result.positionObject = position;
   result.positionWorld = positionWorld.xyz;
   result.position = mul(projView, positionWorld);
   result.normalObject = normal;
   result.normalWorld = normalize(normalWorld.xyz); // must normalize in PS
   result.color = color;
   result.uv = uv;

   return result;
}

struct PSOutput {
   float4 baseColor : SV_TARGET0;
   float4 normalMaterial : SV_TARGET1;
};

PSOutput PSMain(PSInput input) {
   PSOutput result;

   // Color assumed to not have alpha (deferred can't do it well)
   float3 base = input.color.xyz * SampleDiffuseMap(input.uv, input.positionObject.xyz, input.normalObject.xyz).xyz;
   result.baseColor = float4(base, 1.0f);

   float metallic, roughness;
   pbrMaterialProperties(input.positionWorld, metallic, roughness);

   float3 normal = normalize(input.normalWorld);
   float material = pbrDeferredPackMaterial(metallic, roughness);
   result.normalMaterial = float4(normal, material);
   
   return result;
}