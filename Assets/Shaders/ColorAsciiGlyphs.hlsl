#ifndef MOON_GAME_COLOR_ASCII_GLYPHS_INCLUDED
#define MOON_GAME_COLOR_ASCII_GLYPHS_INCLUDED

uint2 AsciiGlyph(uint glyph)
{
    switch (glyph)
    {
        case 0u: return uint2(0x08000000u, 0x1u); // .
        case 1u: return uint2(0x08401080u, 0x0u); // :
        case 2u: return uint2(0x88401080u, 0x0u); // ;
        case 3u: return uint2(0x000f8000u, 0x0u); // -
        case 4u: return uint2(0x44222110u, 0x0u); // /
        case 5u: return uint2(0x88421804u, 0x3u); // i
        case 6u: return uint2(0x084f9080u, 0x0u); // +
        case 7u: return uint2(0x22a22a20u, 0x0u); // x
        case 8u: return uint2(0x01f07c00u, 0x0u); // =
        case 9u: return uint2(0x1d18b800u, 0x0u); // o
        case 10u: return uint2(0xc444422eu, 0x7u); // 2
        case 11u: return uint2(0xa307843fu, 0x3u); // 5
        case 12u: return uint2(0x2aefbaa0u, 0x0u); // *
        case 13u: return uint2(0x631fc62eu, 0x4u); // A
        case 14u: return uint2(0x95f52beau, 0x2u); // #
        case 15u: return uint2(0x83daf62eu, 0x3u); // @
        default: return uint2(0u, 0u);
    }
}

#endif
