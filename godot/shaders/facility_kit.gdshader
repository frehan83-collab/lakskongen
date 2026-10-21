shader_type spatial;
render_mode cull_back, diffuse_burley, specular_schlick_ggx;
// The farm-types facility kit's one material (LK_FacilityKit, F2-25; F2-20
// extends the per-instance part): the trim atlas, the per-instance build
// progress, lid swatch, band, leg stretch and rotor, the night lights and the
// mirror pass are documented in include/facility_kit_body.gdshaderinc, which
// facility_kit_build.gdshader shares double-sided for shells under
// construction.
#include "res://shaders/include/facility_kit_body.gdshaderinc"
