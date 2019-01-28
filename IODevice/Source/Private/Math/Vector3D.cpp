/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#include "Math/Vector.h"

void DevelopHelper::Vector::operator=(const Vector& rhs)
{
    this->X = rhs.X;
    this->Y = rhs.Y;
    this->Z = rhs.Z;
}

bool DevelopHelper::operator==(const Vector & lhs, const Vector & rhs)
{
    return lhs.X == rhs.X
        &&lhs.Y == rhs.Y
        &&lhs.Z == rhs.Z;
}

bool DevelopHelper::operator!=(const Vector & lhs, const Vector & rhs)
{
    return lhs.X != rhs.X
        || lhs.Y != rhs.Y
        || lhs.Z != rhs.Z;
}
