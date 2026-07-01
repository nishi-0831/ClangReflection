// ColliderType.generated.h
#pragma once

#include <nlohmann/json.hpp>
// ============================================================================
// ColliderTypeの状態を保存するState構造体の定義、Undo/Redoに使うMementoのusing宣言
// ============================================================================
struct ColliderTypeState
{
			ColliderType TYPE_SPHERE;
			ColliderType TYPE_AABB;
			ColliderType TYPE_OBB;
};

// クラスの前方宣言
	class ColliderType;



	

NLOHMANN_JSON_SERIALIZE_ENUM( ColliderType,{
		{
			ColliderType::TYPE_SPHERE,
			"TYPE_SPHERE"
		},
		{
			ColliderType::TYPE_AABB,
			"TYPE_AABB"
		},
		{
			ColliderType::TYPE_OBB,
			"TYPE_OBB"
		},
}
)