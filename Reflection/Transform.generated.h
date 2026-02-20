// Transform.generated.h
#pragma once

#include <nlohmann/json.hpp>
#include "JsonConverter.h"
#include "MTImGui.h"
#include <string>
// ============================================================================
// Transformの状態を保存するState構造体の定義、Undo/Redoに使うMementoのusing宣言
// ============================================================================
#define MT_COMPONENT_Transform() \
	struct TransformState \
	{ \
			 position_; \
			string str; \
	}; \
	class Transform;\
	using TransformMemento = ComponentMemento<Transform, TransformState>;

// ============================================================================
// TransformとTransformMementoの相互変換処理を実装
// ============================================================================
#define MT_GENERATED_BODY_Transform() \
	public: \
	using Memento = TransformMemento; \
	TransformMemento* SaveToMemento() \
	{ \
	OnPreSave(); \
		TransformState state; \
		state.position_ = this->position_; \
		state.str = this->str; \
		return new ComponentMemento<Transform, TransformState>(GetEntityId(), state); \
	} \
	\
	void RestoreFromMemento(const ComponentMemento<Transform, TransformState>& _memento) \
	{ \
		const TransformState& state = _memento.GetState(); \
		this->position_ = state.position_; \
		this->str = state.str; \
		OnPostRestore(); \
	} \
	\
	friend struct Transform_Register; \
	friend void to_json(nlohmann::json& _j,const Transform& _target) \
	{ \
		_j["position_"] = JsonConverter::Serialize<>(_target.position_); \
		_j["str"] = JsonConverter::Serialize<string>(_target.str); \
	} \
	friend void from_json(const nlohmann::json& _j, Transform& _target) \
	{ \
		JsonConverter::Deserialize<>(_target.position_, _j,"position_"); \
		JsonConverter::Deserialize<string>(_target.str, _j,"str"); \
		_target.OnPostRestore(); \
	} \
	static std::string TypeName(){ return "Transform" ;} \
	/* ImGui表示処理の登録 */ \
	static void RegisterImGui() \
	{ \
		static bool registered = false; \
		if (registered) return; \
		registered = true; \
		\
		RegisterShowFuncHolder::Set<Transform>([]( Transform* _target, const char* _name) \
			{ \
				PropertyDisplayRegistry::Instance().ShowProperty(&_target->position_, "position_"); \
				PropertyDisplayRegistry::Instance().ShowProperty(&_target->str, "str"); \
			}); \
		MTImGui::Instance().RegisterComponentViewer<Transform>(); \
	}

#pragma warning(push)
#pragma warning(disable:4005)
// マクロ上書き
#define MT_COMPONENT() MT_COMPONENT_Transform()
#define MT_GENERATED_BODY() MT_GENERATED_BODY_Transform()
#pragma warning(pop)