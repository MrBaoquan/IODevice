/** Copyright (c) mrma617@gmail.com
 *  Author: MrBaoquan
 *  CreateTime: 2018-6-27 9:00
 */

#pragma once
#include <vector>
#include "RawIO.h"

namespace DevelopHelper
{

class CustomIOBase : public RawIO
{
public:
    CustomIOBase() :RawIO() {  }
    CustomIOBase(uint8 InID, uint8 InIndex, std::string InType) :RawIO(InID, InType), deviceIndex(InIndex) { }

    virtual void Tick(float DeltaSeconds) override;

    virtual const bool Valid() const override { return false; }

    virtual void OnFrameEnd() override;

	virtual void Initialize() override;
protected:
    std::vector<ButtonState> channelsState;
    uint8 deviceIndex;
    std::vector<BYTE> DIStatus;

protected:
    void Constructor();

};

};
