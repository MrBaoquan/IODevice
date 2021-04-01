/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#pragma once
#include "IOExportsAPI.h"
#include "CoreTypes.inl"

namespace IOToolkit
{


    struct IOAPI Vector
    {
        Vector() :X(0.f), Y(0.f),Z(0.f) {}
        Vector(float InX, float InY, float InZ) :X(InX), Y(InY),Z(InZ) {}
        Vector(const Vector& rhs) :X(rhs.X), Y(rhs.Y),Z(rhs.Z) {};

        void operator=(const Vector& rhs);

        friend bool operator==(const Vector& lhs, const Vector& rhs);
        friend bool operator!=(const Vector& lhs, const Vector&rhs);

        float X;
        float Y;
        float Z;
    };



}