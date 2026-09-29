SELECT * FROM Relics WHERE json_array_length(CommonRewardIds) < 2 OR json_array_Length(UncommonRewardIds) < 1 OR RarerewardId IS NULL;
-- This script is just a quick test for identifying if any rewards are missing.
-- Note that this accounts for formas missing by default (hence checking for <2 and <1 instead of <3 and <2).
-- If a relic doesn't contain a forma but is missing 1 other thing, this won't catch it