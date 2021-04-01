/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#pragma once
namespace IOToolkit
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