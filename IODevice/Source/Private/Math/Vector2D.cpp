/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#include "Math/Vector2D.h"

void IOToolkit::Vector2D::operator=(const Vector2D& rhs)
{
    this->X = rhs.X;
    this->Y = rhs.Y;
}

bool IOToolkit::operator==(const Vector2D & lhs, const Vector2D & rhs)
{
    return lhs.X == rhs.X
        &&lhs.Y == rhs.Y;
}

bool IOToolkit::operator!=(const Vector2D & lhs, const Vector2D & rhs)
{
    return lhs.X != rhs.X
        || lhs.Y != rhs.Y;
}
