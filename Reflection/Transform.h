#include "Transform.generated.h"
#include "Vector3.h"
#include "Hoge.h"
#include <iostream>

#define MT_STRINGIFY(...) #__VA_ARGS__
#define MT_PROPERTY(...) __attribute__((annotate("MT_PROPERTY," MT_STRINGIFY(__VA_ARGS__))))
#define MT_FUNCTION(...) __attribute__((annotate("MT_FUNCTION," MT_STRINGIFY(__VA_ARGS__))))
#define MT_COMPONENT(...) __attribute__((annotate("MT_COMPONENT," MT_STRINGIFY(__VA_ARGS__))))
#define MT_GENERATED_BODY()



class MT_COMPONENT() Transform
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
};