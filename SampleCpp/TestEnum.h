#pragma once
#include "ReflectionMacro.h"


enum class [[MT_ENUM()]] ColliderType
{
	TYPE_SPHERE, // 球(中心からの一定距離)
	TYPE_AABB,	 // 軸並行境界ボックス(各軸に平行な辺)
	TYPE_OBB
};
