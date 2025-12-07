#include "Transform.generated.h"
#include "Vector3.h"
#include "Hoge.h"
#include <iostream>
#define MT_PROPERTY()
#define MT_FUNCTION()
#define MT_COMPONENT()
#define MT_GENERATED_BODY()



MT_COMPONENT()
class Transform
{
public:
	MT_GENERATED_BODY()
protected:
	void OnPostRestore() override;
private:
	MT_PROPERTY()
	Vector3 position_;
	MT_PROPERTY()
	Hoge hoge_;
	// 既存のコード(省略)
	//Matrix4x4 matrixWorld_;
};