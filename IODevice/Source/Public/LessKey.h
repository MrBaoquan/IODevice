#pragma once
namespace DevelopHelper
{

class LessKey
{
public:
    bool operator()(const FKey& lhs, const FKey& rhs) const
    {
        return std::string(lhs.GetName()) < std::string(rhs.GetName());
    }
};


};