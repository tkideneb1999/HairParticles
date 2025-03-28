# HairParticles
This project is a currently very basic implementation of strand-based Hair Rendering.
It takes lines as input guide curves and then duplicates it during tessellation in a circular way.
Screenspace Polygons are then generates in the Geometry Shader.
The Pixel shader uses a basic Kajiya-Kay Tangent based shading model to give specular highlights to the resulting Hair Strands.

![](https://raw.githubusercontent.com/tkideneb1999/HairParticles/refs/heads/main/Pictures/Picture_1.png)