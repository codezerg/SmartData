// @ts-check
import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';

// https://astro.build/config
export default defineConfig({
	integrations: [
		starlight({
			title: 'SmartData',
			description: 'A .NET data framework with AutoRepo ORM, binary RPC, schema migration, and an embedded admin console.',
			social: [
				{ icon: 'github', label: 'GitHub', href: 'https://github.com/codezerg/SmartData' },
			],
			components: {
				SocialIcons: './src/components/TopNav.astro',
			},
			sidebar: [
				{
					label: 'Overview',
					items: [
						{ label: 'Introduction', slug: 'index' },
						{ label: 'Architecture', slug: 'overview/architecture' },
					],
				},
				{
					label: 'Get Started',
					items: [
						{ label: 'Install', slug: 'get-started/install' },
						{ label: 'Your first procedure', slug: 'get-started/your-first-procedure' },
						{ label: 'Your first RPC call', slug: 'get-started/your-first-rpc-call' },
					],
				},
				{
					label: 'Fundamentals',
					items: [
						{ label: 'Procedures', slug: 'fundamentals/procedures' },
						{ label: 'Entities & AutoRepo', slug: 'fundamentals/entities' },
						{ label: 'Database context', slug: 'fundamentals/database-context' },
						{ label: 'Binary RPC', slug: 'fundamentals/binary-rpc' },
						{ label: 'Providers', slug: 'fundamentals/providers' },
						{ label: 'Scheduling', slug: 'fundamentals/scheduling' },
						{ label: 'Tracking & Ledger', slug: 'fundamentals/tracking' },
					],
				},
				{
					label: 'How to…',
					items: [
						{ label: 'Define a procedure', slug: 'how-to/define-a-procedure' },
						{ label: 'Add a new entity', slug: 'how-to/add-a-new-entity' },
						{ label: 'Return DTOs, not entities', slug: 'how-to/return-dtos-not-entities' },
						{ label: 'Schedule a recurring job', slug: 'how-to/schedule-a-recurring-job' },
						{ label: 'Enable change tracking', slug: 'how-to/enable-change-tracking' },
						{ label: 'Query entity history', slug: 'how-to/query-entity-history' },
						{ label: 'Register a provider', slug: 'how-to/register-a-provider' },
						{ label: 'Back up a database', slug: 'how-to/back-up-a-database' },
						{ label: 'Write a custom provider', slug: 'how-to/write-a-custom-provider' },
						{ label: 'Call procedures from a client', slug: 'how-to/call-procedures-from-a-client' },
						{ label: 'Use the admin console', slug: 'how-to/use-the-admin-console' },
					],
				},
				{
					label: 'Tutorials',
					items: [
						{ label: 'Build a CRUD app', slug: 'tutorials/build-a-crud-app' },
						{ label: 'Migrate an existing schema', slug: 'tutorials/migrate-an-existing-schema' },
					],
				},
				{
					label: 'Reference',
					items: [
						{ label: 'SmartData.Server', slug: 'reference/smartdata-server' },
						{ label: 'SmartData.Server.Sqlite', slug: 'reference/smartdata-server-sqlite' },
						{ label: 'SmartData.Server.SqlServer', slug: 'reference/smartdata-server-sqlserver' },
						{ label: 'SmartData.Client', slug: 'reference/smartdata-client' },
						{ label: 'SmartData.Core', slug: 'reference/smartdata-core' },
						{ label: 'SmartData.Console', slug: 'reference/smartdata-console' },
						{ label: 'SmartData.Cli (sd)', slug: 'reference/smartdata-cli' },
						{ label: 'System procedures', slug: 'reference/system-procedures' },
					],
				},
				{
					label: 'Samples',
					items: [
						{ label: 'Overview', slug: 'samples' },
					],
				},
			],
		}),
	],
});
