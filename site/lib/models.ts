import { z } from 'zod';

export const sections = ['progression','quests','items','abilities','world','crafting','knowledge','events','tools'] as const;
export const SectionSchema = z.enum(sections);
export const SourceSchema = z.object({ url:z.string().url(), author:z.string(), retrievedAt:z.string().nullable(), sourceUpdatedAt:z.string().nullable().default(null) });
export const EvidenceSchema = z.object({ value:z.string(), source:z.string().url(), locator:z.string().optional(), variant:z.string().optional() });
export const FactSchema = z.object({ label:z.string(), value:z.string().nullable(), evidence:z.array(EvidenceSchema).default([]), alternatives:z.array(EvidenceSchema).default([]) });
export const RequirementSchema = z.object({ insight:z.number().int().min(1).max(99).nullable().default(null), ability:z.number().int().min(1).nullable().default(null), mastery:z.enum(['required','not-required','unknown']).default('unknown'), path:z.enum(['pure','subpath','either','unknown']).default('unknown'), classes:z.array(z.string()).default([]), text:z.string().default('') });
export const AssetSchema = z.object({ path:z.string().startsWith('/assets/'), width:z.number().positive().nullable(), height:z.number().positive().nullable(), alt:z.string(), source:z.string().url() });
export const TableSchema = z.object({ title:z.string(), headers:z.array(z.string()), rows:z.array(z.array(z.string())), source:z.string().url().optional(),sources:z.array(z.string().url()).optional() });
export const RankSchema = z.object({ name:z.string(), rank:z.number().int().positive().nullable(), requirements:RequirementSchema, facts:z.array(FactSchema), summary:z.string(), sources:z.array(SourceSchema) });
export const VariantSchema = z.object({ name:z.string(), kind:z.enum(['enchantment','consecration','quality','random-stats','other']), evidence:z.string().url(), facts:z.array(FactSchema).default([]) });
const Common = {
 id:z.string().min(1), slug:z.string().min(1), title:z.string().min(1), section:SectionSchema,
 summary:z.string(), aliases:z.array(z.string()).default([]), sources:z.array(SourceSchema),
 facts:z.array(FactSchema).default([]), notes:z.array(z.string()).default([]),
 steps:z.array(z.string()).default([]), tables:z.array(TableSchema).default([]),
 assets:z.array(AssetSchema).default([]), related:z.array(z.string()).default([]),
 tags:z.array(z.string()).default([]), historical:z.boolean().default(false),
 review:z.enum(['rewritten','structured','index-only','needs-review']).default('structured'),
};
export const ItemSchema=z.object({...Common,kind:z.literal('item'),enchantable:z.boolean().nullable(),variants:z.array(VariantSchema),requirements:RequirementSchema});
export const AbilitySchema=z.object({...Common,kind:z.literal('ability'),abilityType:z.enum(['skill','spell']),ranks:z.array(RankSchema)});
export const QuestSchema=z.object({...Common,kind:z.literal('quest'),requirements:RequirementSchema,rewards:z.array(z.string())});
export const EventSchema=z.object({...Common,kind:z.literal('event'),requirements:RequirementSchema,rewards:z.array(z.string())});
export const LocationSchema=z.object({...Common,kind:z.literal('location'),mapId:z.string().nullable()});
export const NpcSchema=z.object({...Common,kind:z.literal('npc'),coordinates:z.string().nullable()});
export const MonsterSchema=z.object({...Common,kind:z.literal('monster'),requirements:RequirementSchema});
export const RecipeSchema=z.object({...Common,kind:z.literal('recipe'),materials:z.array(z.object({name:z.string(),quantity:z.number().nullable()})),output:z.string()});
export const ArticleSchema=z.object({...Common,kind:z.literal('article')});
export const ClassSchema=z.object({...Common,kind:z.literal('class'),baseClass:z.string(),advancedClass:z.string(),requirements:RequirementSchema});
export const EntrySchema=z.discriminatedUnion('kind',[ItemSchema,AbilitySchema,QuestSchema,EventSchema,LocationSchema,NpcSchema,MonsterSchema,RecipeSchema,ArticleSchema,ClassSchema]);
export const RelationSchema=z.object({from:z.string(),to:z.string(),type:z.enum(['requires','rewards','material','crafts','sold-by','drops','found-in','trained-by','connects','related']),source:z.string().url(),detail:z.string().optional()});
export const LibrarySchema=z.object({generatedAt:z.string(),entries:z.array(EntrySchema),relations:z.array(RelationSchema),coverage:z.object({discovered:z.number(),fetched:z.number(),excluded:z.number(),unavailable:z.number(),deferred:z.number(),represented:z.number(),rewritten:z.number()}),discrepancies:z.array(z.object({entry:z.string(),field:z.string(),preferred:z.string(),alternatives:z.array(EvidenceSchema)}))});
export type Entry=z.infer<typeof EntrySchema>;
export type Fact=z.infer<typeof FactSchema>;
export type Relation=z.infer<typeof RelationSchema>;
export type Library=z.infer<typeof LibrarySchema>;
