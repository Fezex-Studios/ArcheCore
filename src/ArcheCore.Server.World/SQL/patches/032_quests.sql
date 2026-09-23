-- A two-quest chain and the NPC who gives it.
-- Needs the AddQuests migration (Quests' new columns + QuestObjectives).
-- Safe to re-run.
--
-- ObjectiveType: 1 Kill (NpcTemplates.Id), 2 Collect (Items.item_id), 3 Talk (NpcTemplates.Id)

-- ── The quest giver: Bren, next to Hilda ──
INSERT OR IGNORE INTO NpcTemplates
    (Id, Name, Level, ModelType, InteractRange, IsStationary, MaxHealth, LootTableId, RespawnSeconds, Title, Greeting,
     AggroRadius, AttackRange, AttackCooldownMs, AttackDamageMin, AttackDamageMax) VALUES
    (11, 'Quartermaster Bren', 12, 'NPC_ORC', 4.0, 1, 0, 0, 0, 'Quartermaster',
     'Every pair of hands helps, friend.', 0, 2.5, 2000, 0, 0);

INSERT OR IGNORE INTO NpcSpawners (Id, TemplateId, X, Y, Z, Count, Radius) VALUES
    (11, 11, -354.0, 0.0, 2520.0, 1, 0.0);

-- F opens his quest list, G is ordinary conversation.
INSERT OR IGNORE INTO InteractableActions (Id, TargetKind, TemplateId, Slot, ActionType, Label, IconName, CursorName, IsEnabled) VALUES
    (10, 1, 11, 0, 7, 'Quests', 'action_quest', 'talk', 1),
    (11, 1, 11, 1, 1, 'Talk',   'action_talk',  'talk', 1);

-- ── Quest 1: gather ──
INSERT OR IGNORE INTO Quests
    (Id, Name, Description, AcceptText, CompleteText, GiverNpcTemplateId, TurnInNpcTemplateId,
     MinLevel, RequiredQuestId, RewardGold, RewardItemId, RewardItemQuantity) VALUES
    (1, 'Iron for the Forge',
     'Bren needs iron ore from the veins east of the meadow.',
     'Five lumps should do it. The veins are east of here.',
     'That''ll keep the forge lit. Here, you''ve earned this.',
     11, 11, 1, 0, 60, 4, 2);

INSERT OR IGNORE INTO QuestObjectives (Id, QuestId, SortOrder, ObjectiveType, TargetId, RequiredCount, Text) VALUES
    (1, 1, 0, 2, 7, 5, 'Collect Iron Ore');

-- ── Quest 2: kill, and only after quest 1 ──
INSERT OR IGNORE INTO Quests
    (Id, Name, Description, AcceptText, CompleteText, GiverNpcTemplateId, TurnInNpcTemplateId,
     MinLevel, RequiredQuestId, RewardGold, RewardItemId, RewardItemQuantity) VALUES
    (2, 'Thin the Camp',
     'The orcs east of the meadow are getting bold.',
     'Three of them should make the rest think twice. Mind yourself - they hit back.',
     'Good work. That''ll quiet them for a while.',
     11, 11, 1, 1, 120, 6, 1);

INSERT OR IGNORE INTO QuestObjectives (Id, QuestId, SortOrder, ObjectiveType, TargetId, RequiredCount, Text) VALUES
    (2, 2, 0, 1, 1, 3, 'Slay Orc Grunts');
