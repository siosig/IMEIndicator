#include "models/BackgroundImageSettings.h"

#include <gtest/gtest.h>

using namespace imeindicator::models;

// 013-ime-corner-image: contracts/settings-schema-v4.md "backgroundImage" サブスキーマ
// および data-model.md §1 / §バリデーションサマリ の検証。

// (1) 既定値: isVisible は false（v3 以前のファイルを読んだ直後も false になる前提）
TEST(BackgroundImageSettingsTests, DefaultIsVisibleIsFalse)
{
    BackgroundImageSettings s;
    EXPECT_FALSE(s.isVisible);
}

// (2) to_json → from_json の往復で一致（true / false 両方）。
//     出力キーは camelCase の "isVisible" 1 つで、型は bool。
TEST(BackgroundImageSettingsTests, RoundTripPreservesIsVisible)
{
    for (const bool visible : {false, true}) {
        BackgroundImageSettings src;
        src.isVisible = visible;

        nlohmann::json j;
        to_json(j, src);
        ASSERT_TRUE(j.is_object());
        ASSERT_TRUE(j.contains("isVisible"));
        EXPECT_TRUE(j["isVisible"].is_boolean());
        EXPECT_EQ(j.size(), 1u);

        BackgroundImageSettings dst;
        dst.isVisible = !visible;   // 往復で必ず上書きされることを確認するため逆値で初期化
        from_json(j, dst);
        EXPECT_EQ(dst, src);
        EXPECT_EQ(dst.isVisible, visible);
    }
}

// (3) キー欠落 {} → false。
//     既存値が true でも from_json が明示的に false へ戻すこと（読み飛ばしで残らない）を確認。
TEST(BackgroundImageSettingsTests, MissingKeyFallsBackToFalse)
{
    nlohmann::json j = nlohmann::json::object();

    BackgroundImageSettings s;
    s.isVisible = true;
    EXPECT_NO_THROW(from_json(j, s));
    EXPECT_FALSE(s.isVisible);
}

// (4) 型不正 {"isVisible":"yes"} → false（例外にしない）。
//     手編集で混入しやすい数値 1 も bool とは見なさない。
TEST(BackgroundImageSettingsTests, InvalidTypeFallsBackToFalse)
{
    const nlohmann::json cases[] = {
        {{"isVisible", "yes"}},
        {{"isVisible", 1}},
        {{"isVisible", nullptr}},
    };

    for (const auto& j : cases) {
        BackgroundImageSettings s;
        s.isVisible = true;
        EXPECT_NO_THROW(from_json(j, s)) << j.dump();
        EXPECT_FALSE(s.isVisible) << j.dump();
    }
}

// 将来予約キー（size / margin / imagePath）を含む未知キーは無視し、isVisible だけ採用する
// （settings-schema-v4.md "backgroundImage サブスキーマ"）。
TEST(BackgroundImageSettingsTests, UnknownKeysAreIgnored)
{
    nlohmann::json j = {
        {"isVisible", true},
        {"size", 128},
        {"margin", 16},
        {"imagePath", "custom.png"},
        {"someFutureField", "ignored"}
    };

    BackgroundImageSettings s;
    EXPECT_NO_THROW(from_json(j, s));
    EXPECT_TRUE(s.isVisible);
}

// (5) operator== は isVisible の差で false になる（AppSettings::operator== の合成に必要）
TEST(BackgroundImageSettingsTests, EqualityDistinguishesIsVisible)
{
    BackgroundImageSettings a;
    BackgroundImageSettings b;
    EXPECT_EQ(a, b);

    b.isVisible = true;
    EXPECT_NE(a, b);

    a.isVisible = true;
    EXPECT_EQ(a, b);
}

// clamp() は現状 no-op（bool のみのため値域制約なし）。値を変えないことを確認。
TEST(BackgroundImageSettingsTests, ClampIsNoOp)
{
    BackgroundImageSettings s;
    s.isVisible = true;
    s.clamp();
    EXPECT_TRUE(s.isVisible);

    s.isVisible = false;
    s.clamp();
    EXPECT_FALSE(s.isVisible);
}
