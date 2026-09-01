namespace ExpertToJob.Tools.DemoRoster;

/// <summary>One industry's prose fragments: template strings with {slot} placeholders.</summary>
/// <param name="Standard">Everyday CV voice.</param>
/// <param name="AcronymHeavy">
/// Deliberately protocol/product-name-dense variants (FIX 4.4, HL7/FHIR, Unity ECS, ...) used
/// for ~10-15% of the roster.
/// </param>
public sealed record IndustryNarratives(
    IReadOnlyList<string> ExpertSummaries,
    NarrativeTemplateGroup Standard,
    NarrativeTemplateGroup AcronymHeavy);

public sealed record NarrativeTemplateGroup(
    IReadOnlyList<string> Summaries,
    IReadOnlyList<string> Achievements);

/// <summary>
/// Hand-authored career narrative building blocks, two template groups per industry.
/// The committed dataset is assembled combinatorially from these: every summary template
/// carries a {company} slot plus at least two randomized slots, so 500 experts stay
/// textually distinct without hand-writing 500 CVs.
/// </summary>
public static class NarrativeFragments
{
    public static IndustryNarratives For(string industryId) =>
        ByIndustry.TryGetValue(industryId, out var narratives)
            ? narratives
            : throw new ArgumentException($"No narrative fragments for industry '{industryId}'", nameof(industryId));

    private static readonly IReadOnlyDictionary<string, IndustryNarratives> ByIndustry =
        new Dictionary<string, IndustryNarratives>
        {
            ["fintech"] = new(
                ExpertSummaries:
                [
                    "Payments-focused backend engineer with {yrs}+ years across ledgers, settlement and risk; deep {skill} experience and a habit of leaving audit trails cleaner than found.",
                    "{yrs} years building money-movement systems — ledgers, card processing, reconciliation — with strong {skill} and a bias for boring, provable correctness.",
                    "Backend engineer who has spent {yrs} years in regulated fintech, from merchant onboarding to settlement, comfortable owning services end to end and explaining them to auditors.",
                ],
                Standard: new(
                    Summaries:
                    [
                        "Owned the core payments ledger at {company}, coordinating a team of {team} engineers and keeping month-end settlement runs under {ms} per transaction batch.",
                        "Built and operated {company}'s merchant onboarding platform, automating compliance checks that previously took {qty} analyst-hours per week and cutting time-to-first-payment {pct}%.",
                        "Led the modernisation of {company}'s account services from a batch mainframe feed to event-driven services processing {kqty} transactions a day across {qty} product lines.",
                        "Backend engineer on {company}'s lending desk, designing credit-decision services that cut approval turnaround {pct}% for {kqty} applications a year while holding default rates flat.",
                    ],
                    Achievements:
                    [
                        "Redesigned the reconciliation engine, shrinking the nightly close window from {qty} hours to {qty} minutes.",
                        "Scaled the payment-authorisation path to {kqty} requests per day with p99 latency under {ms}.",
                        "Cut chargeback processing costs {pct}% by automating dispute-evidence collection for {qty} card programs.",
                        "Introduced idempotent retry semantics across settlement services, eliminating {qty} duplicate postings a month across {qty} settlement services.",
                        "Mentored {team} engineers through the migration from batch settlement to streaming ledger updates over {months} months.",
                        "Reduced false-positive fraud declines {pct}% with a rules-plus-scoring hybrid engine reviewed by {team} risk analysts.",
                        "Delivered multi-currency wallet support across {qty} corridors in {months} months.",
                        "Drove the double-entry ledger rewrite that survived a {x} Black Friday traffic spike while cutting settlement lag {pct}%.",
                        "Brought {skill} expertise in-house, cutting vendor spend {pct}% a year across {qty} integrations.",
                    ]),
                AcronymHeavy: new(
                    Summaries:
                    [
                        "Maintained {company}'s FIX 4.4 order gateway and the ISO 20022 migration of its payment rails, keeping certification current across {qty} counterparties and {qty} venues.",
                        "Ran PCI-DSS Level 1 compliance engineering at {company}: tokenisation vault, HSM key ceremonies, and quarterly ASV scan remediation across {qty} in-scope services for {team} teams.",
                        "Built {company}'s SEPA Instant stack on ISO 20022 pacs.008/pacs.002 flows with PCI-DSS-scoped card-on-file tokenisation serving {kqty} payments a day.",
                    ],
                    Achievements:
                    [
                        "Migrated the FIX 4.4 gateway to FIX 5.0 SP2 with zero failed conformance cases across {qty} counterparties and {qty} certification suites.",
                        "Re-platformed SWIFT MT flows to ISO 20022 pacs messages across {qty} message types, {months} months ahead of the CBPR+ deadline.",
                        "Passed PCI-DSS 4.0 re-certification with zero major findings across {qty} in-scope services and {qty} controls.",
                        "Cut FIX session failover to under {ms} using warm-standby sequence-number persistence across {qty} sessions.",
                        "Implemented 3-D Secure 2.2 challenge flows, lifting frictionless approval rates {pct}% across {qty} issuers.",
                        "Built pain.001 ingestion for corporate bulk payments, validating {kqty} instructions a day from {qty} corporates.",
                        "Consolidated HSM-backed key management for PAN tokenisation across {team} product teams and {qty} services.",
                    ])),

            ["gaming"] = new(
                ExpertSummaries:
                [
                    "Engine and gameplay programmer with {yrs} years shipping console and PC titles; happiest deep in profilers, {skill} pipelines and simulation code.",
                    "{yrs} years in game development from prototypes to live ops, strong in {skill} and the performance work that keeps frame budgets honest.",
                    "Game developer who has shipped {qty} titles over {yrs} years, spanning gameplay systems, tools and multiplayer services.",
                ],
                Standard: new(
                    Summaries:
                    [
                        "Gameplay programmer on {company}'s flagship co-op title, owning combat systems and the ability framework used by {qty} designer-built encounters across {qty} biomes.",
                        "Built core engine systems at {company} — streaming, save/load, and a job scheduler that improved worst-case frame times {pct}% across {qty} shipped platforms.",
                        "Worked across {company}'s live-ops stack, shipping seasonal content pipelines that cut content lead time from {qty} weeks to {qty} days.",
                        "Owned matchmaking and session services for {company}'s arena shooter, keeping queue times under {qty} seconds at {kqty} concurrent players.",
                    ],
                    Achievements:
                    [
                        "Rewrote the animation state machine, cutting blend glitches reported by QA {pct}% across {qty} character rigs.",
                        "Shipped {qty} gameplay features across two seasonal releases and {qty} platform targets.",
                        "Profiled draw-call batching, cutting median frame time {pct}% on min-spec hardware across {qty} benchmark scenes.",
                        "Built the replay system used by {kqty} players a month for clip sharing, in {months} months of part-time effort.",
                        "Led a strike team of {team} through crunch-free delivery of a {months}-month expansion spanning {qty} content drops.",
                        "Cut client patch sizes {pct}% with content-addressed asset bundles spanning {kqty} assets.",
                        "Moved server ticks to a fixed-point simulation, ending desync bugs across {qty} ranked seasons and {kqty} matches.",
                        "Standardised {skill} tooling for the content team, saving {qty} hours of build wrangling a week.",
                        "Halved load times on last-gen consoles by streaming {kqty} assets through a rebuilt IO layer in {months} months.",
                    ]),
                AcronymHeavy: new(
                    Summaries:
                    [
                        "Ported {company}'s simulation core to Unity ECS/DOTS, hitting a stable 60 FPS with {kqty} active entities on mid-range hardware across {qty} game modes.",
                        "Wrote HLSL shader libraries and URP render features at {company}, including a GPU-driven foliage system covering {qty} biomes and {kqty} instances per frame.",
                        "Owned {company}'s netcode: rollback prediction over a Unity ECS simulation with server rewind, tested to {qty} ms of artificial latency across {qty} playtests.",
                    ],
                    Achievements:
                    [
                        "Migrated gameplay systems to Unity ECS, cutting main-thread time {pct}% in {qty}-actor crowd scenes.",
                        "Authored an HLSL compute pipeline for GPU skinning of {kqty} instanced characters at {pct}% less bandwidth.",
                        "Built Burst-compiled pathfinding jobs handling {kqty} agents at 60 FPS on {qty}-core machines.",
                        "Refactored HLSL includes and stripped dead keywords, reducing shader variant count {pct}% and build times {pct}%.",
                        "Implemented deterministic rollback netcode on Unity ECS, surviving {qty} ranked seasons and {kqty} matches without a desync incident.",
                        "Moved skinned-mesh culling to HLSL compute, reclaiming {pct}% GPU time on base consoles across {qty} scenes.",
                        "Cut Unity ECS structural-change stalls {pct}% by batching entity commands across {qty} systems.",
                    ])),

            ["healthtech"] = new(
                ExpertSummaries:
                [
                    "Health-informatics engineer with {yrs} years wiring EHRs, labs and imaging together; fluent in {skill} and the realities of hospital IT.",
                    "{yrs} years in clinical software: patient portals, integration engines and data pipelines, with {skill} depth and a compliance-first habit.",
                    "Engineer who has spent {yrs} years making medical systems talk to each other reliably, safely and auditably across {qty} provider organisations.",
                ],
                Standard: new(
                    Summaries:
                    [
                        "Integration engineer at {company}, connecting hospital EHRs to a scheduling platform used across {qty} clinics and {kqty} appointments a month.",
                        "Built patient-facing services at {company} — intake, consent and messaging — raising portal adoption {pct}% in {months} months.",
                        "Owned {company}'s clinical data pipeline, normalising lab feeds from {qty} source systems into one longitudinal record covering {kqty} patients.",
                        "Backend engineer for {company}'s remote-monitoring platform, ingesting device vitals for {kqty} patients under audit requirements spanning {qty} jurisdictions.",
                    ],
                    Achievements:
                    [
                        "Cut patient-record merge errors {pct}% with deterministic-plus-probabilistic matching across {kqty} records.",
                        "Delivered e-prescription integration across {qty} pharmacy networks in {months} months.",
                        "Reduced clinician click-depth for common workflows {pct}%, validated in usability rounds with {team} departments.",
                        "Automated eligibility checks, saving front-desk staff {qty} hours a week across {qty} sites.",
                        "Built consent-tracking with immutable audit trails covering {kqty} record accesses a month across {qty} facilities.",
                        "Led the on-call redesign that took integration incidents from weekly to {qty} per quarter across {qty} interfaces.",
                        "Migrated {kqty} historical encounters into the new record store with zero reconciliation gaps in {months} months.",
                        "Introduced {skill} test harnesses that caught {qty} mapping regressions before they reached wards.",
                        "Shortened lab-result turnaround alerts from {qty} hours to {qty} minutes.",
                    ]),
                AcronymHeavy: new(
                    Summaries:
                    [
                        "Ran {company}'s interoperability layer: HL7 v2 ADT/ORU feeds and a FHIR R4 facade serving {qty} downstream consumers at {kqty} messages a day.",
                        "Built {company}'s imaging exchange on DICOMweb (QIDO/WADO-RS) with FHIR ImagingStudy indexing across {qty} sites and {kqty} studies.",
                        "Owned SMART on FHIR app integrations at {company}, including CDS Hooks ordering advice wired into {qty} hospital EHRs across {qty} health systems.",
                    ],
                    Achievements:
                    [
                        "Mapped HL7 v2.5 ORU^R01 lab feeds from {qty} analysers into FHIR Observation resources with {pct}% fewer manual corrections.",
                        "Stood up a FHIR R4 server that passed Inferno ONC certification on the first run, serving {qty} client apps and {kqty} requests a day.",
                        "Cut DICOM study retrieval latency to {ms} via pre-fetching against modality worklists at {qty} sites.",
                        "Implemented HL7 MLLP high-availability listeners processing {kqty} messages a day across {qty} interfaces.",
                        "Shipped SMART on FHIR OAuth scopes for {qty} third-party apps without a single audit finding in {yrs} years.",
                        "Converted CCDA archives into FHIR Bundles for {kqty} patients during a {months}-month EHR migration.",
                        "Automated IHE connectathon test suites, cutting integration certification time {pct}% across {qty} profiles.",
                    ])),

            ["e-commerce"] = new(
                ExpertSummaries:
                [
                    "Full-stack commerce engineer, {yrs} years from storefront to fulfilment; strong {skill}, obsessive about conversion funnels and page speed.",
                    "{yrs} years building online retail platforms — search, checkout, promotions — with deep {skill} experience and a merchant's eye for metrics.",
                    "Product-minded engineer with {yrs} years in e-commerce, comfortable owning features from design file to warehouse webhook across {qty} markets.",
                ],
                Standard: new(
                    Summaries:
                    [
                        "Full-stack engineer on {company}'s storefront, owning checkout and cart services that convert {kqty} sessions a day across {qty} markets.",
                        "Led search and discovery at {company}, lifting search-to-cart conversion {pct}% through ranking and facet work across {kqty} SKUs.",
                        "Built {company}'s promotions engine — stacking rules, flash sales and loyalty pricing across {qty} markets and {kqty} daily orders.",
                        "Owned the order-management backend at {company}, orchestrating fulfilment across {qty} warehouses and {qty} 3PL partners.",
                    ],
                    Achievements:
                    [
                        "Raised checkout conversion {pct}% by collapsing the flow to two steps and removing {qty} redundant form fields.",
                        "Cut Largest Contentful Paint to {ms} on product pages, lifting organic traffic {pct}%.",
                        "Scaled Black Friday peak to {x} normal load with queue-based order intake and {qty} oversells across the weekend: all refunded within the hour.",
                        "Rebuilt the recommendations carousel, driving {pct}% of revenue through cross-sell on {kqty} sessions a day.",
                        "Shipped one-click reorder used by {kqty} returning customers a month within {months} months.",
                        "Introduced contract tests between the storefront and {qty} internal APIs, cutting integration bugs {pct}%.",
                        "Localised the storefront into {qty} languages with a translation pipeline that cut copy turnaround {pct}%.",
                        "Reduced cart-abandonment email unsubscribes {pct}% by switching {qty} campaigns to behaviour-triggered sends.",
                        "Took product-page build times from {qty} minutes to {qty} seconds with incremental static regeneration.",
                    ]),
                AcronymHeavy: new(
                    Summaries:
                    [
                        "Ran {company}'s storefront platform: Next.js ISR, GraphQL federation over gRPC backends, and WCAG 2.2 AA compliance across {qty} templates and {qty} markets.",
                        "Built {company}'s headless commerce APIs — a GraphQL gateway fronting gRPC order and inventory services at {kqty} requests a day for {qty} client apps.",
                        "Owned payments UX at {company}: PSD2 SCA flows, 3-D Secure fallbacks and WCAG 2.2 checkout accessibility across {qty} markets and {kqty} daily sessions.",
                    ],
                    Achievements:
                    [
                        "Federated {qty} GraphQL subgraphs over gRPC services without breaking {qty} existing client integrations.",
                        "Hit WCAG 2.2 AA across checkout, verified with NVDA and VoiceOver runs on {qty} releases and {qty} templates.",
                        "Cut p95 add-to-cart latency to {ms} by moving inventory reads to gRPC streaming caches across {qty} regions.",
                        "Implemented PSD2 SCA exemption logic, recovering {pct}% of one-click conversions across {qty} issuers.",
                        "Moved image delivery to AVIF with responsive srcsets, cutting page weight {pct}% on {qty} template types.",
                        "Shipped Core Web Vitals fixes that took CLS below 0.05 on {qty} template types, lifting SEO traffic {pct}%.",
                        "Replaced REST polling with gRPC server streams for order tracking used by {kqty} sessions a day across {qty} storefronts.",
                    ])),

            ["embedded"] = new(
                ExpertSummaries:
                [
                    "Embedded engineer with {yrs} years across firmware, RTOS bring-up and hardware debugging; at home with {skill} and an oscilloscope.",
                    "{yrs} years of firmware development for shipped hardware — power management, comms stacks and the discipline of {skill}.",
                    "Engineer who has spent {yrs} years within kilobytes of RAM, making {kqty} devices boot fast, sip power and update safely.",
                ],
                Standard: new(
                    Summaries:
                    [
                        "Firmware engineer at {company}, owning motor-control and sensor-fusion code for a product line of {qty} SKUs shipping {kqty} units a year.",
                        "Built battery-management firmware at {company}, extending field battery life {pct}% across {kqty} deployed units.",
                        "Owned the OTA update pipeline at {company}, shipping signed firmware to {kqty} devices with staged rollouts across {qty} hardware revisions.",
                        "Developed test rigs and HIL automation at {company}, cutting release regression time from {qty} days to {qty} hours.",
                    ],
                    Achievements:
                    [
                        "Cut boot time to {ms} by lazy-initialising peripherals and trimming {qty} startup tasks.",
                        "Reduced field RMA rate {pct}% after root-causing an intermittent brownout reset across {qty} board revisions.",
                        "Brought up {qty} new board revisions in {months} months, from schematic review to production test firmware.",
                        "Squeezed the RAM footprint {pct}% to fit {qty} roadmap features on existing silicon.",
                        "Implemented watchdog-supervised task monitors, taking field lockups down {pct}% across {kqty} devices.",
                        "Built a HIL farm of {qty} rigs running nightly regression across {qty} product variants.",
                        "Delivered secure boot with signed images across {qty} product SKUs in {months} months.",
                        "Cut sleep-mode current draw {pct}%, extending shelf life {x} on battery-powered units.",
                        "Ported legacy firmware to a modern build system, taking clean builds from {qty} minutes to {qty} seconds.",
                    ]),
                AcronymHeavy: new(
                    Summaries:
                    [
                        "Owned {company}'s vehicle gateway firmware: CAN 2.0B/CAN-FD stacks, UDS diagnostics and a FreeRTOS-based message router spanning {qty} ECUs and {qty} vehicle platforms.",
                        "Ported {company}'s sensor platform from bare-metal to Zephyr RTOS with MCUboot secure boot across {qty} ARM Cortex-M variants and {kqty} fielded units.",
                        "Built FreeRTOS telemetry firmware at {company}, streaming MQTT over LTE-M from {kqty} field devices with {qty}-day offline buffering.",
                    ],
                    Achievements:
                    [
                        "Migrated the RTOS layer from FreeRTOS to Zephyr, unifying builds across {qty} boards and {qty} product lines.",
                        "Implemented CAN 2.0B J1939 stacks with guaranteed bus-off recovery under {ms} across {qty} ECUs.",
                        "Cut ISR latency below {qty} microseconds on Cortex-M7 with zero-copy DMA rings, verified on {qty} workloads.",
                        "Shipped MCUboot A/B updates over MQTT to {kqty} devices with automatic rollback and {qty} failed updates to date.",
                        "Profiled FreeRTOS task starvation and restored {pct}% headroom on the control loop across {qty} configurations.",
                        "Passed EMC pre-compliance first try after refactoring the CAN 2.0B transceiver bring-up on {qty} boards across {qty} product lines.",
                        "Wrote Zephyr device drivers for {qty} sensors, {team} of them upstreamed to the mainline tree.",
                    ])),

            ["data-ml"] = new(
                ExpertSummaries:
                [
                    "ML engineer with {yrs} years taking models from notebook to production; strong {skill}, allergic to unmonitored pipelines.",
                    "{yrs} years across data engineering and ML platforms — feature stores, serving, evaluation — with deep {skill} experience.",
                    "Engineer who has spent {yrs} years making machine learning boring: reproducible, observable and {pct}% cheaper to run each year.",
                ],
                Standard: new(
                    Summaries:
                    [
                        "ML engineer at {company}, owning demand-forecasting models that steer {kqty} weekly replenishment decisions across {qty} distribution centres.",
                        "Built {company}'s feature platform — offline/online parity, backfills and lineage for {qty} production features used by {team} teams.",
                        "Ran experimentation infrastructure at {company}: metrics store, guardrails and {qty} concurrent A/B tests over {kqty} daily users.",
                        "Data engineer on {company}'s warehouse, modelling {qty} source systems into marts queried by {kqty} scheduled reports a week.",
                    ],
                    Achievements:
                    [
                        "Lifted forecast accuracy {pct}% (WAPE) by adding promotions and weather covariates across {qty} product categories.",
                        "Cut model training costs {pct}% with spot orchestration and gradient checkpointing across {qty} training jobs.",
                        "Took batch scoring from nightly to hourly for {kqty} entities with {pct}% less warehouse contention.",
                        "Built drift monitors that caught {qty} silent feature breakages across {qty} pipelines before they reached models.",
                        "Reduced pipeline failure pages {pct}% by making {qty} DAGs idempotent and replayable.",
                        "Shipped a retrieval-augmented support assistant deflecting {pct}% of {kqty} monthly tickets.",
                        "Standardised {skill} model packaging, cutting deploy time from {qty} days to {qty} minutes.",
                        "Backfilled {kqty} rows of historical features with bit-exact online/offline parity in {months} months.",
                        "Mentored {team} analysts into writing production-quality transformations across {qty} data marts.",
                    ]),
                AcronymHeavy: new(
                    Summaries:
                    [
                        "Owned {company}'s inference platform: ONNX Runtime on GPU pools behind gRPC, serving {kqty} predictions a day at {ms} p99.",
                        "Built {company}'s RAG stack — pgvector retrieval, cross-encoder re-ranking and ONNX-quantised embedders across {qty} corpora and {kqty} chunks.",
                        "Ran LLM evaluation at {company}: golden sets, judge models and CI regression gates over {qty} prompt suites, with ONNX-exported judges pinned per release.",
                    ],
                    Achievements:
                    [
                        "Quantised transformer encoders to INT8 ONNX, cutting inference cost {pct}% at equal recall across {qty} models.",
                        "Served embeddings over gRPC streaming at {kqty} tokens a second per GPU across {qty} model versions.",
                        "Exported the ranking ensemble to ONNX Runtime, dropping p99 latency to {ms} for {kqty} daily requests.",
                        "Built pgvector HNSW indexes over {kqty} chunks with {ms} retrieval p95.",
                        "Cut hallucination rate {pct}% with citation-grounded generation and answerability gates over {qty} eval suites.",
                        "Distilled a 7B model for edge inference within {qty} MB of memory at {pct}% of teacher quality.",
                        "Automated ONNX opset upgrades across {qty} models with golden-output regression checks over {kqty} stored outputs.",
                    ])),

            ["devops-platform"] = new(
                ExpertSummaries:
                [
                    "Platform engineer with {yrs} years of Kubernetes, Terraform and the human systems around them; strong {skill}, allergic to snowflake servers.",
                    "{yrs} years keeping production boring across clouds — SLOs, GitOps and {skill} — with a paved-road philosophy.",
                    "SRE who has spent {yrs} years automating toil out of existence across {qty} product teams and writing postmortems worth reading.",
                ],
                Standard: new(
                    Summaries:
                    [
                        "Platform engineer at {company}, running the internal developer platform used by {qty} service teams shipping {qty} deploys a day.",
                        "Owned {company}'s Kubernetes estate — {qty} clusters across three clouds — with golden-path templates adopted by {team} teams.",
                        "SRE at {company}, cutting MTTR from {qty} hours to {qty} minutes through runbooks, SLOs and blameless reviews.",
                        "Built {company}'s delivery tooling: ephemeral preview environments and a deploy train shipping {qty} releases a week for {team} teams.",
                    ],
                    Achievements:
                    [
                        "Cut mean deploy time from {qty} minutes to under five with cache-aware pipelines across {qty} repos.",
                        "Reduced cloud spend {pct}% via rightsizing, spot fleets and storage-class hygiene across {qty} accounts.",
                        "Migrated {qty} services to the paved-road platform with zero-downtime cutovers over {months} months.",
                        "Took on-call pages down {pct}% by deleting {qty} noisy alerts and adding SLO burn-rate alerting.",
                        "Ran the incident program: {qty} game days a year and postmortem actions with a {pct}% completion rate.",
                        "Built self-service database provisioning, ending {qty}-day ticket queues for {team} teams.",
                        "Rolled out infra-as-code across {qty} accounts with drift detection and {qty} policy checks in CI.",
                        "Achieved {ms} p99 on the internal artifact registry serving {kqty} pulls a day.",
                        "Standardised {skill} usage across teams, cutting new-service onboarding from {qty} days to under one week.",
                    ]),
                AcronymHeavy: new(
                    Summaries:
                    [
                        "Ran identity for {company}'s platform: OIDC federation, SAML bridges for legacy vendors and short-lived credentials across {qty} clusters and {qty} accounts.",
                        "Built {company}'s zero-trust service mesh — mTLS everywhere, SPIFFE identities and gRPC xDS control-plane config for {qty} services across {qty} clusters.",
                        "Owned {company}'s GitOps stack: ArgoCD app-of-apps, OIDC-scoped RBAC and progressive delivery for {qty} teams and {qty} environments.",
                    ],
                    Achievements:
                    [
                        "Replaced static cloud keys with OIDC workload identity across {qty} pipelines in {months} months.",
                        "Federated SAML and OIDC logins into one IdP, retiring {qty} shadow admin accounts across {qty} tenants.",
                        "Rolled out mTLS with gRPC health-checked load balancing across {qty} mesh services and {qty} namespaces with zero downtime.",
                        "Cut token-exchange latency to {ms} with a caching OIDC introspection sidecar used by {qty} services.",
                        "Automated SCIM provisioning, taking joiner/leaver processing from {qty} days to {qty} minutes.",
                        "Enforced signed build provenance on {qty} pipelines, closing {qty} supply-chain audit findings.",
                        "Migrated ingress to gRPC-aware gateways, ending {qty} keepalive-related outages a quarter for {team} teams.",
                    ])),

            ["mobile"] = new(
                ExpertSummaries:
                [
                    "Mobile engineer with {yrs} years shipping iOS and Android apps people rate highly; deep {skill}, pragmatic about cross-platform choices.",
                    "{yrs} years of mobile development — native and cross-platform — with strong {skill} and a soft spot for buttery animations.",
                    "App developer who has spent {yrs} years sweating cold starts, crash rates and the last {pct}% of UI polish.",
                ],
                Standard: new(
                    Summaries:
                    [
                        "Mobile engineer on {company}'s consumer app with {kqty} monthly actives, owning the ordering flow end to end across {qty} releases a year.",
                        "Built {company}'s mobile design system — {qty} components shared across iOS and Android feature teams, adopted by {team} squads.",
                        "Led offline-first sync at {company}, keeping field crews productive through {qty}-hour connectivity gaps across {kqty} devices.",
                        "Owned release engineering for {company}'s apps: trains, feature flags and staged rollouts to {kqty} devices every {qty} weeks.",
                    ],
                    Achievements:
                    [
                        "Raised crash-free sessions above 99.9% across {qty} releases in {months} months.",
                        "Cut cold-start time {pct}% by deferring {qty} startup tasks off the main thread.",
                        "Shipped the redesigned onboarding, lifting day-7 retention {pct}% for {kqty} new users a month.",
                        "Reduced app size {pct}% with on-demand resources and audits across {qty} asset packs.",
                        "Migrated {qty} screens to declarative UI in {months} months without pausing feature delivery.",
                        "Built deep-link routing that lifted campaign conversion {pct}% across {qty} entry points.",
                        "Introduced snapshot tests across {qty} components, cutting release-week visual regressions {pct}%.",
                        "Cut ANR rate {pct}% by moving disk IO off the main thread behind {skill} abstractions.",
                        "Shipped accessibility passes reaching full screen-reader coverage on {qty} core flows in {months} months.",
                    ]),
                AcronymHeavy: new(
                    Summaries:
                    [
                        "Built {company}'s app platform: gRPC-backed BFFs, OIDC PKCE auth and WCAG 2.2-aligned accessibility across {qty} flows and {qty} feature teams.",
                        "Owned auth at {company}: OIDC with PKCE, biometric unlock and token refresh that survives {qty}-day offline stretches for {kqty} users.",
                        "Migrated {company}'s REST clients to gRPC with protobuf codegen shared across iOS, Android and {qty} backend services at {kqty} requests a day.",
                    ],
                    Achievements:
                    [
                        "Replaced bespoke auth with OIDC PKCE flows, cutting login failures {pct}% for {kqty} monthly actives.",
                        "Moved sync to gRPC bidirectional streams, cutting battery drain {pct}% in {qty}-device field tests.",
                        "Hit WCAG 2.2 AA on {qty} core journeys, verified with TalkBack and VoiceOver scripts across {qty} releases.",
                        "Cut payload sizes {pct}% with protobuf over gRPC replacing JSON polling across {qty} endpoints.",
                        "Implemented certificate pinning and OIDC token binding without breaking {kqty} active sessions across {qty} app versions.",
                        "Automated store submissions with {qty}-lane pipelines, taking release day from {qty} hours to a non-event.",
                        "Brought push-notification opt-in up {pct}% with pre-permission UX tested across {qty} cohorts.",
                    ])),

            ["gov-enterprise"] = new(
                ExpertSummaries:
                [
                    "Enterprise engineer with {yrs} years in public-sector delivery; strong {skill}, fluent in procurement realities and audit trails.",
                    "{yrs} years modernising systems that cannot fail politically or technically, with deep {skill} experience across {qty} programmes.",
                    "Engineer who has spent {yrs} years turning legacy estates into maintainable platforms, one strangler facade at a time.",
                ],
                Standard: new(
                    Summaries:
                    [
                        "Software engineer at {company}, modernising a citizen-services portal handling {kqty} applications a year across {qty} service lines.",
                        "Led integration work at {company}, replacing {qty} point-to-point interfaces with a governed service layer used by {team} delivery teams.",
                        "Built case-management workflows at {company} for {qty} regional offices, with full audit and records-retention support for {kqty} cases a year.",
                        "Owned the reporting platform at {company}, consolidating {qty} legacy databases into one governed warehouse serving {qty} departments.",
                    ],
                    Achievements:
                    [
                        "Cut application processing time {pct}% by digitising {qty} paper-first workflows.",
                        "Delivered the legacy migration — {kqty} records — with a {months}-month dual-run period and zero data-loss findings.",
                        "Passed the annual security audit with zero critical findings, {qty} years running, across {qty} systems.",
                        "Reduced report generation from overnight batches to {qty} minutes for {qty} statutory reports.",
                        "Wrote the interface control documents that made {qty} vendor integrations independently testable, cutting integration defects {pct}%.",
                        "Introduced automated regression suites to a codebase that had none, reaching {pct}% coverage on {qty} core flows.",
                        "Rolled out role-based access aligned to {qty} job functions across {team} departments.",
                        "Kept a {yrs}-year-old system alive while strangling it into {qty} replaceable services.",
                        "Trained {team} staff engineers on the new platform with runbooks and {months} months of paired rotations.",
                    ]),
                AcronymHeavy: new(
                    Summaries:
                    [
                        "Owned identity modernisation at {company}: SAML federation with {qty} agencies and an OIDC broker for citizen-facing apps serving {kqty} accounts.",
                        "Ran {company}'s Keycloak estate — OIDC clients for {qty} applications, SAML bridges for legacy suites and mandatory MFA for {kqty} users.",
                        "Built {company}'s document exchange: qualified digital signatures, SAML-secured portals and retention policies across {qty} record classes and {qty} agencies.",
                    ],
                    Achievements:
                    [
                        "Federated {qty} agencies through SAML 2.0 with attribute-based access mapping and {qty} shared roles.",
                        "Migrated {kqty} citizen accounts to OIDC over {months} months with passwordless options and no measurable support spike.",
                        "Rolled out Keycloak in high availability across {qty} environments with fully automated realm configuration for {qty} applications.",
                        "Implemented OIDC step-up authentication for high-risk transactions, cutting fraud cases {pct}% across {qty} transaction types.",
                        "Retired {qty} legacy login systems behind a single SAML/OIDC broker in {months} months.",
                        "Achieved WCAG 2.2 AA on the citizen portal, confirmed by an external accessibility audit of {qty} journeys across {qty} releases.",
                        "Cut single-sign-on incident tickets {pct}% after rebuilding session lifetimes and logout flows across {qty} apps.",
                    ])),

            ["agency"] = new(
                ExpertSummaries:
                [
                    "Agency engineer with {yrs} years shipping fast, polished work for demanding brands; strong {skill}, comfortable with ambiguous briefs.",
                    "{yrs} years of client-side delivery across dozens of stacks, deepest in {skill}, pragmatic about scope and deadlines.",
                    "Full-stack generalist who has spent {yrs} years making {qty} launches a year land on time without cutting corners that show.",
                ],
                Standard: new(
                    Summaries:
                    [
                        "Full-stack developer at {company}, delivering {qty} client sites and apps a year across retail, culture and B2B accounts in {qty} sectors.",
                        "Led builds at {company} for household-name campaigns, including a launch that took {kqty} visitors in its first week across {qty} regions.",
                        "Owned {company}'s internal starter kits, cutting project setup from {qty} days to {qty} minutes across the studio.",
                        "Senior engineer at {company} pairing with designers to ship award-entered work on {qty}-week timelines for {team} concurrent accounts.",
                    ],
                    Achievements:
                    [
                        "Delivered {qty} production launches a year with a two-person engineering pod across {qty} accounts.",
                        "Cut page weight {pct}% on a flagship campaign site, hitting {ms} LCP on throttled 3G.",
                        "Built a headless CMS setup reused across {qty} client projects, saving {qty} setup days each.",
                        "Rescued a failing engagement mid-flight and shipped within {qty} weeks, keeping a {yrs}-year account worth {qty} launches a year.",
                        "Turned a one-off build into a {yrs}-year retainer through post-launch iteration and {qty} quarterly roadmaps.",
                        "Standardised accessibility checklists adopted on {qty} projects, cutting audit findings {pct}%.",
                        "Prototyped {qty} pitch concepts a year, converting {team} into signed engagements.",
                        "Mentored {team} juniors through their first {qty} production launches.",
                        "Introduced visual regression testing, cutting client-reported UI bugs {pct}% across {qty} retainers.",
                    ]),
                AcronymHeavy: new(
                    Summaries:
                    [
                        "Ran {company}'s accessibility practice: WCAG 2.2 AA audits, remediation sprints and training across {qty} client engagements and {team} internal teams.",
                        "Built headless storefronts at {company} — Next.js ISR, Storefront API integrations and WCAG 2.2 compliance for {qty} brands across {qty} markets.",
                        "Owned auth-heavy client builds at {company}: OIDC SSO into member portals and paywalled content for {qty} publishers with {kqty} subscribers.",
                    ],
                    Achievements:
                    [
                        "Took {qty} client sites to WCAG 2.2 AA with documented audit trails and {pct}% fewer re-test findings.",
                        "Integrated OIDC SSO across a publisher's {qty} properties and {kqty} subscribers in one quarter with zero forced logouts.",
                        "Hit {ms} LCP on a campaign microsite that survived a {x} launch-day traffic spike.",
                        "Automated WCAG 2.2 checks in CI, catching {pct}% of issues before manual audit on {qty} projects.",
                        "Shipped a Storefront API build handling {kqty} sessions a day during launch week across {qty} regions.",
                        "Rebuilt a legacy member portal behind OIDC without logging out a single one of {kqty} users across {qty} member tiers.",
                        "Cut studio monorepo build times {pct}% with remote caching across {qty} projects.",
                    ])),
        };
}
