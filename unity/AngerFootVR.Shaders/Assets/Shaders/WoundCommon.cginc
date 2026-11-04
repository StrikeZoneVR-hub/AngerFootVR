// Anger Foot VR's wounds, shared by the two wound passes. A wound is analytic, in world space, set every frame from the
// bone it rides on (so it bends and ragdolls with the body, and the goon's own mesh and texture are never touched):
//   _WA: entry point xyz, entry radius (a bite: its centre and radius)
//   _WB: the shot's way into the body xyz (unit), how far through it went (0: it stopped inside)
//   _WC: exit radius, pit depth (a wound that stopped), kind (0 shot, 1 bite, 2 bruise; +4 on the torso: ribs), age (s)
//   _WD: a reference direction across the shot xyz (the body's up: the ribs run across it), seed
// Drips (blood running down from a wound, fixed to the body once they've run):
//   _DA: start xyz, width;  _DB: down xyz (unit), length now;  _DC: across xyz (unit), seed;  _DD: out of the skin xyz, age
#ifndef ANGERFOOTVR_WOUNDS
#define ANGERFOOTVR_WOUNDS

#define MAXW 12
#define MAXD 16

float4 _WA[MAXW];
float4 _WB[MAXW];
float4 _WC[MAXW];
float4 _WD[MAXW];
float _WCount;
float4 _DA[MAXD];
float4 _DB[MAXD];
float4 _DC[MAXD];
float4 _DD[MAXD];
float _DCount;

sampler2D _NoiseTex;
sampler2D _SkinNoise;
float4 _BloodColor;

// the ragged edge: a wound's radius, torn by its own noise round its mouth (angle 0..1)
float Torn(float angle01, float seed, float amount)
{
    float n = tex2Dlod(_SkinNoise, float4(angle01 * 2.0 + seed, seed * 0.37, 0, 0)).r;
    float m = tex2Dlod(_NoiseTex, float4(angle01 * 5.0 + seed * 1.3, seed * 0.71, 0, 0)).b;
    return 1.0 + amount * ((n - 0.5) * 1.6 + (m - 0.5) * 0.5);
}

// where a point is round a wound's axis: its distance from the axis, how far along it, the angle (0..1)
void AroundAxis(float3 p, float3 e, float3 u, float3 refv, out float d, out float along, out float angle01, out float3 perp, out float2 flat)
{
    float3 w = p - e;
    along = dot(w, u);
    perp = w - along * u;
    d = length(perp);
    float3 x = normalize(refv - dot(refv, u) * u + 1e-5);
    float3 y = cross(u, x);
    flat = float2(dot(perp, x), dot(perp, y));
    angle01 = atan2(flat.y, flat.x) * 0.15915494 + 0.5;
}

#endif
