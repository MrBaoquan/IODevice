#include "Math/Vector2D.h"

void DevelopHelper::Vector2D::operator=(const Vector2D& rhs)
{
    this->X = rhs.X;
    this->Y = rhs.Y;
}

bool DevelopHelper::operator==(const Vector2D & lhs, const Vector2D & rhs)
{
    return lhs.X == rhs.X
        &&lhs.Y == rhs.Y;
}

bool DevelopHelper::operator!=(const Vector2D & lhs, const Vector2D & rhs)
{
    return lhs.X != rhs.X
        || lhs.Y != rhs.Y;
}
