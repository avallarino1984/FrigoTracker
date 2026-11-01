-- Frigo Tracker: one shared household (inventory + grocery list) for several signed-in users.
-- Tables are prefixed with frigo_ so they can live in the same Supabase project as other apps.
-- Run once in Supabase: SQL Editor -> New query -> paste -> Run. Safe to run again.

-- ---------- Tables ----------

create table if not exists public.frigo_households (
    id         uuid primary key default gen_random_uuid(),
    name       text not null,
    -- Short code given to the other person so they can join the household from the app.
    join_code  text not null unique,
    created_at timestamptz not null default now()
);

create table if not exists public.frigo_members (
    household_id uuid not null references public.frigo_households (id) on delete cascade,
    user_id      uuid not null default auth.uid() references auth.users (id) on delete cascade,
    joined_at    timestamptz not null default now(),
    primary key (household_id, user_id)
);

create table if not exists public.frigo_items (
    id           uuid primary key,
    household_id uuid not null references public.frigo_households (id) on delete cascade,
    name         text not null,
    category     text not null default 'Other',
    location     text not null default 'Fridge',
    quantity     numeric not null default 1,
    unit         text not null default '',
    stored_on    date not null default current_date,
    expires_on   date,
    notes        text,
    updated_at   timestamptz not null default now(),
    updated_by   uuid default auth.uid()
);

create table if not exists public.frigo_grocery (
    id           uuid primary key,
    household_id uuid not null references public.frigo_households (id) on delete cascade,
    name         text not null,
    quantity     text,
    checked      boolean not null default false,
    updated_at   timestamptz not null default now(),
    updated_by   uuid default auth.uid()
);

create index if not exists frigo_items_household_idx on public.frigo_items (household_id);
create index if not exists frigo_grocery_household_idx on public.frigo_grocery (household_id);
create index if not exists frigo_members_user_idx on public.frigo_members (user_id);

-- The server clock and the real author win over whatever the client sends.
create or replace function public.frigo_touch()
returns trigger
language plpgsql
set search_path = ''
as $$
begin
    new.updated_at := now();
    new.updated_by := auth.uid();
    return new;
end;
$$;

drop trigger if exists frigo_items_touch on public.frigo_items;
create trigger frigo_items_touch before insert or update on public.frigo_items
    for each row execute function public.frigo_touch();

drop trigger if exists frigo_grocery_touch on public.frigo_grocery;
create trigger frigo_grocery_touch before insert or update on public.frigo_grocery
    for each row execute function public.frigo_touch();

-- ---------- Membership helper ----------

-- security definer so the policies below can check membership without recursing into frigo_members' own policy.
create or replace function public.frigo_is_member(hid uuid)
returns boolean
language sql
stable
security definer
set search_path = ''
as $$
    select exists (
        select 1 from public.frigo_members m
        where m.household_id = hid and m.user_id = auth.uid()
    );
$$;

-- ---------- Row Level Security ----------

alter table public.frigo_households enable row level security;
alter table public.frigo_members enable row level security;
alter table public.frigo_items enable row level security;
alter table public.frigo_grocery enable row level security;

-- Households: visible and renamable by their members; created and joined only through the functions below.
drop policy if exists "frigo_households_select" on public.frigo_households;
create policy "frigo_households_select" on public.frigo_households
    for select to authenticated using (public.frigo_is_member(id));

drop policy if exists "frigo_households_update" on public.frigo_households;
create policy "frigo_households_update" on public.frigo_households
    for update to authenticated using (public.frigo_is_member(id)) with check (public.frigo_is_member(id));

-- Members: you see the members of your household and can leave it yourself.
drop policy if exists "frigo_members_select" on public.frigo_members;
create policy "frigo_members_select" on public.frigo_members
    for select to authenticated using (public.frigo_is_member(household_id));

drop policy if exists "frigo_members_delete_self" on public.frigo_members;
create policy "frigo_members_delete_self" on public.frigo_members
    for delete to authenticated using (user_id = auth.uid());

-- Items and grocery list: full access for the household's members only.
drop policy if exists "frigo_items_all" on public.frigo_items;
create policy "frigo_items_all" on public.frigo_items
    for all to authenticated
    using (public.frigo_is_member(household_id))
    with check (public.frigo_is_member(household_id));

drop policy if exists "frigo_grocery_all" on public.frigo_grocery;
create policy "frigo_grocery_all" on public.frigo_grocery
    for all to authenticated
    using (public.frigo_is_member(household_id))
    with check (public.frigo_is_member(household_id));

-- ---------- Functions called by the app (POST /rest/v1/rpc/...) ----------

-- Creates a household with a random 6-character join code and makes the caller its first member.
create or replace function public.frigo_create_household(p_name text)
returns public.frigo_households
language plpgsql
security definer
set search_path = ''
as $$
declare
    result public.frigo_households;
begin
    if auth.uid() is null then
        raise exception 'Non connecté';
    end if;

    insert into public.frigo_households (name, join_code)
    values (coalesce(nullif(trim(p_name), ''), 'Maison'),
            upper(substr(replace(gen_random_uuid()::text, '-', ''), 1, 6)))
    returning * into result;

    insert into public.frigo_members (household_id, user_id) values (result.id, auth.uid());
    return result;
end;
$$;

-- Joins the household that owns this code (case-insensitive). Joining twice is harmless.
create or replace function public.frigo_join_household(p_code text)
returns public.frigo_households
language plpgsql
security definer
set search_path = ''
as $$
declare
    result public.frigo_households;
begin
    if auth.uid() is null then
        raise exception 'Non connecté';
    end if;

    select * into result from public.frigo_households
    where join_code = upper(trim(p_code));

    if result.id is null then
        raise exception 'Code de foyer inconnu';
    end if;

    insert into public.frigo_members (household_id, user_id) values (result.id, auth.uid())
    on conflict do nothing;
    return result;
end;
$$;

-- ---------- Grants ----------
-- Explicit Data API access (required for tables created after 2026-10-30).
-- No grant to anon: the app requires a signed-in user. RLS above still filters the rows.

revoke all on public.frigo_households, public.frigo_members, public.frigo_items, public.frigo_grocery from anon;

grant select, update on public.frigo_households to authenticated;
grant select, delete on public.frigo_members to authenticated;
grant select, insert, update, delete on public.frigo_items to authenticated;
grant select, insert, update, delete on public.frigo_grocery to authenticated;

grant select, insert, update, delete
    on public.frigo_households, public.frigo_members, public.frigo_items, public.frigo_grocery
    to service_role;

-- Functions are executable by PUBLIC by default: restrict them to signed-in users.
revoke execute on function public.frigo_create_household(text) from public, anon;
revoke execute on function public.frigo_join_household(text) from public, anon;
revoke execute on function public.frigo_is_member(uuid) from public, anon;
grant execute on function public.frigo_create_household(text) to authenticated;
grant execute on function public.frigo_join_household(text) to authenticated;
grant execute on function public.frigo_is_member(uuid) to authenticated;
