#include "services/ProcessPriorityService.h"

#include <gtest/gtest.h>

#include <algorithm>

using namespace imeindicator::services;

TEST(ProcessPriorityServiceTests, EnumerateDistinctProcessNamesReturnsNonEmpty)
{
    // テスト実行中は少なくとも自プロセスが存在するため非空になる
    auto names = ProcessPriorityService::enumerateDistinctProcessNames();
    EXPECT_GT(names.size(), 0u);
}

TEST(ProcessPriorityServiceTests, EnumerateDistinctProcessNamesNoDuplicates)
{
    auto names = ProcessPriorityService::enumerateDistinctProcessNames();
    // 大文字小文字を区別しない比較で隣接要素に重複なし（set で排除済みのはず）
    for (size_t i = 1; i < names.size(); ++i) {
        EXPECT_NE(::_wcsicmp(names[i - 1].c_str(), names[i].c_str()), 0)
            << L"Duplicate: " << names[i];
    }
}

TEST(ProcessPriorityServiceTests, EnumerateDistinctProcessNamesAreSorted)
{
    auto names = ProcessPriorityService::enumerateDistinctProcessNames();
    EXPECT_TRUE(std::is_sorted(names.begin(), names.end(),
        [](const std::wstring& a, const std::wstring& b) {
            return ::_wcsicmp(a.c_str(), b.c_str()) < 0;
        }));
}
