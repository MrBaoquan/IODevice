/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#pragma once
#include "IOExportsAPI.h"
#include "CoreTypes.inl"

namespace DevelopHelper
{
    

struct IOAPI Vector2D
{
    Vector2D():X(0.f),Y(0.f){}
    Vector2D(float InX,float InY):X(InX),Y(InY){}
    Vector2D(const Vector2D& rhs) :X(rhs.X), Y(rhs.Y) {};

    void operator=(const Vector2D& rhs);

    friend bool operator==(const Vector2D& lhs, const Vector2D& rhs);
    friend bool operator!=(const Vector2D& lhs, const Vector2D&rhs);

    float X;
    float Y;
};



}