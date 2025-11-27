#include <iostream>
#include "Hoge.h"
#define MT_PROPERTY()
#define MT_FUNCTION()
#define MT_COMPONENT()


namespace test
{
	MT_COMPONENT()
	class Sample1
	{
	public :
		enum class EnumType
		{
			E0,
			E1,
			E2,
		};
		MT_PROPERTY()
		int value;
		EnumType e;
		Sample() {};
		~Sample() {};
		MT_FUNCTION()
		void f() {};
	private:
		Hoge hoge;
	};
}