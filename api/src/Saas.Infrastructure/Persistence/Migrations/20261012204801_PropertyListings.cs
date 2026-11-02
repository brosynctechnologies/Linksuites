using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace Saas.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PropertyListings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agency_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    slug = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    logo_url = table.Column<string>(type: "text", nullable: true),
                    phone = table.Column<string>(type: "text", nullable: true),
                    email = table.Column<string>(type: "text", nullable: true),
                    address = table.Column<string>(type: "text", nullable: true),
                    bio = table.Column<string>(type: "text", nullable: true),
                    currency = table.Column<string>(type: "text", nullable: false),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    payout_account = table.Column<string>(type: "jsonb", nullable: true),
                    host_cancellations = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agency_profiles", x => x.id);
                    table.ForeignKey(
                        name: "fk_agency_profiles_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "agent_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "text", nullable: false),
                    slug = table.Column<string>(type: "text", nullable: false),
                    bio = table.Column<string>(type: "text", nullable: true),
                    phone = table.Column<string>(type: "text", nullable: true),
                    whats_app = table.Column<string>(type: "text", nullable: true),
                    licence_no = table.Column<string>(type: "text", nullable: true),
                    photo_url = table.Column<string>(type: "text", nullable: true),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    rating_average = table.Column<double>(type: "double precision", nullable: false),
                    rating_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_profiles", x => x.id);
                    table.ForeignKey(
                        name: "fk_agent_profiles_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "agent_reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_name = table.Column<string>(type: "text", nullable: false),
                    rating = table.Column<int>(type: "integer", nullable: false),
                    comment = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    basis = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    report_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_reviews", x => x.id);
                    table.ForeignKey(
                        name: "fk_agent_reviews_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "amenities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    slug = table.Column<string>(type: "text", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_amenities", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "boosts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    listing_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    days = table.Column<int>(type: "integer", nullable: false),
                    amount_minor = table.Column<long>(type: "bigint", nullable: false),
                    currency = table.Column<string>(type: "text", nullable: false),
                    reference = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    buyer_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_boosts", x => x.id);
                    table.ForeignKey(
                        name: "fk_boosts_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "enquiries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    listing_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "text", nullable: false),
                    phone = table.Column<string>(type: "text", nullable: false),
                    email = table.Column<string>(type: "text", nullable: true),
                    message = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_enquiries", x => x.id);
                    table.ForeignKey(
                        name: "fk_enquiries_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "inspection_slots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    weekday = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    start = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    end = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    slot_minutes = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inspection_slots", x => x.id);
                    table.ForeignKey(
                        name: "fk_inspection_slots_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "inspections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    listing_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seeker_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seeker_name = table.Column<string>(type: "text", nullable: false),
                    seeker_phone = table.Column<string>(type: "text", nullable: true),
                    scheduled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    reminder_sent = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inspections", x => x.id);
                    table.ForeignKey(
                        name: "fk_inspections_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "listing_media",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    listing_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_key = table.Column<string>(type: "text", nullable: false),
                    file_name = table.Column<string>(type: "text", nullable: false),
                    thumb_url = table.Column<string>(type: "text", nullable: true),
                    card_url = table.Column<string>(type: "text", nullable: true),
                    full_url = table.Column<string>(type: "text", nullable: true),
                    width = table.Column<int>(type: "integer", nullable: false),
                    height = table.Column<int>(type: "integer", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    error = table.Column<string>(type: "text", nullable: true),
                    phash = table.Column<long>(type: "bigint", nullable: true),
                    duplicate_of_media_id = table.Column<Guid>(type: "uuid", nullable: true),
                    claimed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_listing_media", x => x.id);
                    table.ForeignKey(
                        name: "fk_listing_media_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "listing_reports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    listing_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reporter_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reporter_key = table.Column<string>(type: "text", nullable: false),
                    reason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    details = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_listing_reports", x => x.id);
                    table.ForeignKey(
                        name: "fk_listing_reports_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "listing_stats",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    listing_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    views = table.Column<int>(type: "integer", nullable: false),
                    whats_app_clicks = table.Column<int>(type: "integer", nullable: false),
                    call_clicks = table.Column<int>(type: "integer", nullable: false),
                    enquiries = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_listing_stats", x => x.id);
                    table.ForeignKey(
                        name: "fk_listing_stats_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "listings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    slug = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    purpose = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    property_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    address = table.Column<string>(type: "text", nullable: false),
                    point = table.Column<Point>(type: "geography (point, 4326)", nullable: false),
                    price_minor = table.Column<long>(type: "bigint", nullable: false),
                    currency = table.Column<string>(type: "text", nullable: false),
                    price_period = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    bedrooms = table.Column<int>(type: "integer", nullable: false),
                    bathrooms = table.Column<int>(type: "integer", nullable: false),
                    toilets = table.Column<int>(type: "integer", nullable: false),
                    size_sqm = table.Column<int>(type: "integer", nullable: true),
                    furnished = table.Column<bool>(type: "boolean", nullable: false),
                    tour_url = table.Column<string>(type: "text", nullable: true),
                    video_url = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    moderation_note = table.Column<string>(type: "text", nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    first_published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expiry_warned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    featured_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    top_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    views_count = table.Column<int>(type: "integer", nullable: false),
                    cleaning_fee_minor = table.Column<long>(type: "bigint", nullable: false),
                    min_nights = table.Column<int>(type: "integer", nullable: false),
                    max_guests = table.Column<int>(type: "integer", nullable: false),
                    check_in_time = table.Column<string>(type: "text", nullable: true),
                    check_out_time = table.Column<string>(type: "text", nullable: true),
                    house_rules = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_listings", x => x.id);
                    table.ForeignKey(
                        name: "fk_listings_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "locations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    slug = table.Column<string>(type: "text", nullable: false),
                    centre = table.Column<Point>(type: "geography (point, 4326)", nullable: true),
                    position = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_locations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "property_types",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    slug = table.Column<string>(type: "text", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_property_types", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "saved_listings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    listing_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_saved_listings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "saved_searches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    filter = table.Column<string>(type: "jsonb", nullable: false),
                    frequency = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    last_sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_saved_searches", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stay_blocks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    listing_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start = table.Column<DateOnly>(type: "date", nullable: false),
                    end = table.Column<DateOnly>(type: "date", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stay_blocks", x => x.id);
                    table.ForeignKey(
                        name: "fk_stay_blocks_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "stays",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    listing_id = table.Column<Guid>(type: "uuid", nullable: false),
                    guest_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    guest_name = table.Column<string>(type: "text", nullable: false),
                    guest_phone = table.Column<string>(type: "text", nullable: true),
                    check_in = table.Column<DateOnly>(type: "date", nullable: false),
                    check_out = table.Column<DateOnly>(type: "date", nullable: false),
                    guests = table.Column<int>(type: "integer", nullable: false),
                    nights = table.Column<int>(type: "integer", nullable: false),
                    nightly_minor = table.Column<long>(type: "bigint", nullable: false),
                    cleaning_minor = table.Column<long>(type: "bigint", nullable: false),
                    total_minor = table.Column<long>(type: "bigint", nullable: false),
                    commission_minor = table.Column<long>(type: "bigint", nullable: false),
                    currency = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    hold_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reference = table.Column<string>(type: "text", nullable: false),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_by = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    refund_minor = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stays", x => x.id);
                    table.ForeignKey(
                        name: "fk_stays_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "verification_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    submitted_by = table.Column<Guid>(type: "uuid", nullable: false),
                    documents = table.Column<string>(type: "jsonb", nullable: false),
                    licence_no = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    reviewed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_verification_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_verification_requests_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "listing_amenities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    listing_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amenity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_listing_amenities", x => x.id);
                    table.ForeignKey(
                        name: "fk_listing_amenities_listings_listing_id",
                        column: x => x.listing_id,
                        principalTable: "listings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_listing_amenities_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "listing_costs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    listing_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    label = table.Column<string>(type: "text", nullable: false),
                    amount_minor = table.Column<long>(type: "bigint", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_listing_costs", x => x.id);
                    table.ForeignKey(
                        name: "fk_listing_costs_listings_listing_id",
                        column: x => x.listing_id,
                        principalTable: "listings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_listing_costs_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_agency_profiles_slug",
                table: "agency_profiles",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_agency_profiles_tenant_id",
                table: "agency_profiles",
                column: "tenant_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_agent_profiles_slug",
                table: "agent_profiles",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_agent_profiles_tenant_id_user_id",
                table: "agent_profiles",
                columns: new[] { "tenant_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_agent_reviews_agent_profile_id_user_id",
                table: "agent_reviews",
                columns: new[] { "agent_profile_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_agent_reviews_status",
                table: "agent_reviews",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_agent_reviews_tenant_id",
                table: "agent_reviews",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_amenities_slug",
                table: "amenities",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_boosts_listing_id_status",
                table: "boosts",
                columns: new[] { "listing_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_boosts_reference",
                table: "boosts",
                column: "reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_boosts_tenant_id",
                table: "boosts",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_enquiries_tenant_id_status_created_at",
                table: "enquiries",
                columns: new[] { "tenant_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_inspection_slots_agent_profile_id_weekday",
                table: "inspection_slots",
                columns: new[] { "agent_profile_id", "weekday" });

            migrationBuilder.CreateIndex(
                name: "ix_inspection_slots_tenant_id",
                table: "inspection_slots",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_inspections_agent_profile_id_scheduled_at",
                table: "inspections",
                columns: new[] { "agent_profile_id", "scheduled_at" });

            migrationBuilder.CreateIndex(
                name: "ix_inspections_seeker_user_id",
                table: "inspections",
                column: "seeker_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_inspections_tenant_id",
                table: "inspections",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_listing_amenities_listing_id_amenity_id",
                table: "listing_amenities",
                columns: new[] { "listing_id", "amenity_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_listing_amenities_tenant_id",
                table: "listing_amenities",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_listing_costs_listing_id",
                table: "listing_costs",
                column: "listing_id");

            migrationBuilder.CreateIndex(
                name: "ix_listing_costs_tenant_id",
                table: "listing_costs",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_listing_media_listing_id_position",
                table: "listing_media",
                columns: new[] { "listing_id", "position" });

            migrationBuilder.CreateIndex(
                name: "ix_listing_media_phash",
                table: "listing_media",
                column: "phash");

            migrationBuilder.CreateIndex(
                name: "ix_listing_media_status_created_at",
                table: "listing_media",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_listing_media_tenant_id",
                table: "listing_media",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_listing_reports_listing_id_reporter_key",
                table: "listing_reports",
                columns: new[] { "listing_id", "reporter_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_listing_reports_status_created_at",
                table: "listing_reports",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_listing_reports_tenant_id",
                table: "listing_reports",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_listing_stats_listing_id_day",
                table: "listing_stats",
                columns: new[] { "listing_id", "day" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_listing_stats_tenant_id",
                table: "listing_stats",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_listings_point",
                table: "listings",
                column: "point")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_listings_status_expires_at",
                table: "listings",
                columns: new[] { "status", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_listings_status_purpose_location_id_price_minor",
                table: "listings",
                columns: new[] { "status", "purpose", "location_id", "price_minor" });

            migrationBuilder.CreateIndex(
                name: "ix_listings_tenant_id_slug",
                table: "listings",
                columns: new[] { "tenant_id", "slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_locations_parent_id_position",
                table: "locations",
                columns: new[] { "parent_id", "position" });

            migrationBuilder.CreateIndex(
                name: "ix_locations_slug",
                table: "locations",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_property_types_slug",
                table: "property_types",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_saved_listings_user_id_listing_id",
                table: "saved_listings",
                columns: new[] { "user_id", "listing_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_saved_searches_frequency_last_sent_at",
                table: "saved_searches",
                columns: new[] { "frequency", "last_sent_at" });

            migrationBuilder.CreateIndex(
                name: "ix_saved_searches_user_id",
                table: "saved_searches",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_stay_blocks_listing_id_start",
                table: "stay_blocks",
                columns: new[] { "listing_id", "start" });

            migrationBuilder.CreateIndex(
                name: "ix_stay_blocks_tenant_id",
                table: "stay_blocks",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_stays_guest_user_id",
                table: "stays",
                column: "guest_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_stays_listing_id_check_in",
                table: "stays",
                columns: new[] { "listing_id", "check_in" });

            migrationBuilder.CreateIndex(
                name: "ix_stays_reference",
                table: "stays",
                column: "reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stays_status_hold_expires_at",
                table: "stays",
                columns: new[] { "status", "hold_expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_stays_tenant_id",
                table: "stays",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_verification_requests_status_created_at",
                table: "verification_requests",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_verification_requests_tenant_id",
                table: "verification_requests",
                column: "tenant_id");

            // The final guard against double-booking a short-let: no two held or confirmed stays on one listing may
            // overlap. Check-out is exclusive, so one guest can leave the morning the next arrives.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");
            migrationBuilder.Sql("""
                ALTER TABLE stays ADD CONSTRAINT stays_no_overlap EXCLUDE USING gist (
                    listing_id WITH =, daterange(check_in, check_out, '[)') WITH &&
                ) WHERE (status IN ('Pending', 'Confirmed'));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agency_profiles");

            migrationBuilder.DropTable(
                name: "agent_profiles");

            migrationBuilder.DropTable(
                name: "agent_reviews");

            migrationBuilder.DropTable(
                name: "amenities");

            migrationBuilder.DropTable(
                name: "boosts");

            migrationBuilder.DropTable(
                name: "enquiries");

            migrationBuilder.DropTable(
                name: "inspection_slots");

            migrationBuilder.DropTable(
                name: "inspections");

            migrationBuilder.DropTable(
                name: "listing_amenities");

            migrationBuilder.DropTable(
                name: "listing_costs");

            migrationBuilder.DropTable(
                name: "listing_media");

            migrationBuilder.DropTable(
                name: "listing_reports");

            migrationBuilder.DropTable(
                name: "listing_stats");

            migrationBuilder.DropTable(
                name: "locations");

            migrationBuilder.DropTable(
                name: "property_types");

            migrationBuilder.DropTable(
                name: "saved_listings");

            migrationBuilder.DropTable(
                name: "saved_searches");

            migrationBuilder.DropTable(
                name: "stay_blocks");

            migrationBuilder.DropTable(
                name: "stays");

            migrationBuilder.DropTable(
                name: "verification_requests");

            migrationBuilder.DropTable(
                name: "listings");
        }
    }
}
