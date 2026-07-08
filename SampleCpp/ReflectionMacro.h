#pragma once
#define MT_STRINGIFY(...) #__VA_ARGS__
#define MT_PROPERTY(...) clang::annotate("MT_PROPERTY," MT_STRINGIFY(__VA_ARGS__))
#define MT_FUNCTION(...) clang::annotate("MT_FUNCTION," MT_STRINGIFY(__VA_ARGS__))
#define MT_COMPONENT(...) clang::annotate("MT_COMPONENT," MT_STRINGIFY(__VA_ARGS__))
#define MT_ENUM(...) clang::annotate("MT_ENUM," MT_STRINGIFY(__VA_ARGS__))
#define MT_GENERATED_BODY()