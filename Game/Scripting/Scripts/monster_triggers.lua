-- Official ETC_run_monster_skill.lua; comments removed, duplicate comparison corrected.
function trigger( monster_handle, target_handle, trigger_index, x, y, layer, is_dungeon_raid_monster )
	local monster_id = get_monster_id( monster_handle )
	if monster_id == 10146002 or monster_id == 10146005 or monster_id == 10147002 or monster_id == 10146008 or monster_id == 10148010 or monster_id == 10148005 or monster_id == 10148008 or monster_id == 10149003 or monster_id == 10149006 or monster_id == 10149009 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, monster_handle )
		end
	elseif monster_id == 10150004 or monster_id == 10150007 or monster_id == 10150010 or monster_id == 10150013 or monster_id == 10151003 or monster_id == 10151006 or monster_id == 10151009 or monster_id == 10151012 or monster_id == 10152003 or monster_id == 10152006 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, monster_handle )
		end
	elseif monster_id == 10152009 or monster_id == 10152012 or monster_id == 10153003 or monster_id == 10153006 or monster_id == 10153009 or monster_id == 10153012 or monster_id == 10154003 or monster_id == 10154006 or monster_id == 10154009 or monster_id == 10154012 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, monster_handle )
		end
	elseif monster_id == 10155003 or monster_id == 10155006 or monster_id == 10155009 or monster_id == 10155012 or monster_id == 10156002 or monster_id == 10156005 or monster_id == 10156008 or monster_id == 10156011 or monster_id == 10157002 or monster_id == 10157005 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, monster_handle )
		end
	elseif monster_id == 10157008 or monster_id == 10157011 or monster_id == 10157014 or monster_id == 10158003 or monster_id == 10159002 or monster_id == 10160002 or monster_id == 10161002 or monster_id == 10161005 or monster_id == 10162004 or monster_id == 10163003 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, monster_handle )
		end
	elseif monster_id == 10164002 or monster_id == 10166002 or monster_id == 10167003 or monster_id == 10168002 or monster_id == 10169002 or monster_id == 10169005 or monster_id == 10166004 or monster_id == 10167007 or monster_id == 10168006 or monster_id == 10170003 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, monster_handle )
		end
	elseif monster_id == 10172001 or monster_id == 10175001 or monster_id == 10180001 or monster_id == 10181002 or monster_id == 10183001 or monster_id == 10183004 or monster_id == 10184003 or monster_id == 10185003 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, monster_handle )
		end
	elseif monster_id == 10146003 or monster_id == 10146006 or monster_id == 10147003 or monster_id == 10147006 or monster_id == 10148003 or monster_id == 10148006 or monster_id == 10148009 or monster_id == 10149004 or monster_id == 10149007 or monster_id == 10130001 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		end
	elseif monster_id == 10150005 or monster_id == 10150008 or monster_id == 10150011 or monster_id == 10151001 or monster_id == 10151004 or monster_id == 10151007 or monster_id == 10151010 or monster_id == 10152001 or monster_id == 10152004 or monster_id == 10152007 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		end
	elseif monster_id == 10152010 or monster_id == 10153001 or monster_id == 10153004 or monster_id == 10153007 or monster_id == 10153010 or monster_id == 10154001 or monster_id == 10154004 or monster_id == 10154007 or monster_id == 10154010 or monster_id == 10155001 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		end
	elseif monster_id == 10155004 or monster_id == 10155007 or monster_id == 10155010 or monster_id == 10155013 or monster_id == 10156003 or monster_id == 10156006 or monster_id == 10156009 or monster_id == 10156012 or monster_id == 10157003 or monster_id == 10157006 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		end
	elseif monster_id == 10157009 or monster_id == 10157012 or monster_id == 10157015 or monster_id == 10158004 or monster_id == 10159003 or monster_id == 10160003 or monster_id == 10161003 or monster_id == 10162002 or monster_id == 10162005 or monster_id == 10163004 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		end
	elseif monster_id == 10165002 or monster_id == 10166003 or monster_id == 10167004 or monster_id == 10168003 or monster_id == 10169003 or monster_id == 10165004 or monster_id == 10166005 or monster_id == 10167008 or monster_id == 10169006 or monster_id == 10170004 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		end
	elseif monster_id == 10173001 or monster_id == 10175002 or monster_id == 10180002 or monster_id == 10182001 or monster_id == 10183002 or monster_id == 10184001 or monster_id == 10185001 or monster_id == 10185004 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		end
	elseif monster_id == 10146004 or monster_id == 10146007 or monster_id == 10147004 or monster_id == 10147007 or monster_id == 10148004 or monster_id == 10148007 or monster_id == 10148011 or monster_id == 10149005 or monster_id == 10149008 or monster_id == 10150003 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, monster_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 10150006 or monster_id == 10150009 or monster_id == 10150012 or monster_id == 10151002 or monster_id == 10151005 or monster_id == 10151008 or monster_id == 10151011 or monster_id == 10152002 or monster_id == 10152005 or monster_id == 10152008 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, monster_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 10152011 or monster_id == 10153002 or monster_id == 10153005 or monster_id == 10153008 or monster_id == 10153011 or monster_id == 10154002 or monster_id == 10154005 or monster_id == 10154008 or monster_id == 10154011 or monster_id == 10155002 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, monster_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 10155005 or monster_id == 10155008 or monster_id == 10155011 or monster_id == 10156001 or monster_id == 10156004 or monster_id == 10156007 or monster_id == 10156010 or monster_id == 10157001 or monster_id == 10157004 or monster_id == 10157007 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, monster_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 10157010 or monster_id == 10157013 or monster_id == 10158001 or monster_id == 10158005 or monster_id == 10159004 or monster_id == 10160004 or monster_id == 10161004 or monster_id == 10162003 or monster_id == 10163002 or monster_id == 10164001 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, monster_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 10165003 or monster_id == 10167002 or monster_id == 10167005 or monster_id == 10168004 or monster_id == 10169004 or monster_id == 10165005 or monster_id == 10167006 or monster_id == 10168005 or monster_id == 10170002 or monster_id == 10170005 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, monster_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 10174001 or monster_id == 10175003 or monster_id == 10181001 or monster_id == 10182002 or monster_id == 10183003 or monster_id == 10184002 or monster_id == 10185002 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, monster_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 376 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, monster_handle )
		end
	elseif monster_id == 377 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		end
	elseif monster_id == 378 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, monster_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 10158002 or monster_id == 10170001 or monster_id == 10190001 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, monster_handle )
		end
	elseif monster_id == 5 then
		if trigger_index == 0 then
			monster_skill_cast( 2, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 3, monster_handle, monster_handle )
		end
	elseif monster_id == 8 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, monster_handle )
		end
	elseif monster_id == 9 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 25 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 26 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 13 then
		if trigger_index == 0 then
			monster_skill_cast( 2, monster_handle, target_handle )
		elseif trigger_index == 1 then
			respawn_near_monster( monster_handle, 2055001, 2 )
		end
	elseif monster_id == 14 then
		if trigger_index == 0 then
			monster_skill_cast( 2, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 3, monster_handle, target_handle )
		end
	elseif monster_id == 15 then
		if trigger_index == 0 then
			monster_skill_cast( 2, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 3, monster_handle, target_handle )
		end
	elseif monster_id == 90 then
		if trigger_index == 0 then
			monster_skill_cast( 2, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 3, monster_handle, target_handle )
		end
	elseif monster_id == 91 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, monster_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		elseif trigger_index == 2 then
			monster_skill_cast( 2, monster_handle, target_handle )
		elseif trigger_index == 3 then
			monster_skill_cast( 3, monster_handle, target_handle )
		elseif trigger_index == 4 then
			monster_skill_cast( 4, monster_handle, target_handle )
		elseif trigger_index == 5 then
			monster_skill_cast( 5, monster_handle, target_handle )
		end
	elseif monster_id == 104 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, monster_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		elseif trigger_index == 2 then
			monster_skill_cast( 2, monster_handle, target_handle )
		elseif trigger_index == 3 then
			monster_skill_cast( 3, monster_handle, target_handle )
		elseif trigger_index == 4 then
			monster_skill_cast( 4, monster_handle, target_handle )
		elseif trigger_index == 5 then
			monster_skill_cast( 5, monster_handle, target_handle )
		end
	elseif monster_id == 105 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, monster_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		elseif trigger_index == 2 then
			monster_skill_cast( 2, monster_handle, target_handle )
		elseif trigger_index == 3 then
			monster_skill_cast( 3, monster_handle, target_handle )
		elseif trigger_index == 4 then
			monster_skill_cast( 4, monster_handle, target_handle )
		elseif trigger_index == 5 then
			monster_skill_cast( 5, monster_handle, target_handle )
		end
	elseif monster_id == 53013 or monster_id == 53015 or monster_id == 73005 or monster_id == 73007 or monster_id == 77006 or monster_id == 77008 or monster_id == 81006 or monster_id == 81008 or monster_id == 85005 or monster_id == 85007 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, monster_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		elseif trigger_index == 2 then
			monster_skill_cast( 2, monster_handle, target_handle )
		elseif trigger_index == 3 then
			monster_skill_cast( 3, monster_handle, target_handle )
		elseif trigger_index == 4 then
			monster_skill_cast( 4, monster_handle, target_handle )
		elseif trigger_index == 5 then
			monster_skill_cast( 5, monster_handle, target_handle )
		end
	elseif monster_id == 53014 or monster_id == 53016 or monster_id == 73006 or monster_id == 73008 or monster_id == 77007 or monster_id == 77010 or monster_id == 81007 or monster_id == 81009 or monster_id == 85006 or monster_id == 85008 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, monster_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		elseif trigger_index == 2 then
			monster_skill_cast( 2, monster_handle, target_handle )
		elseif trigger_index == 3 then
			monster_skill_cast( 3, monster_handle, target_handle )
		elseif trigger_index == 4 then
			monster_skill_cast( 4, monster_handle, target_handle )
		elseif trigger_index == 5 then
			monster_skill_cast( 5, monster_handle, target_handle )
		end
	elseif monster_id == 76006 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, monster_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		elseif trigger_index == 2 then
			monster_skill_cast( 2, monster_handle, target_handle )
		elseif trigger_index == 3 then
			monster_skill_cast( 3, monster_handle, target_handle )
		elseif trigger_index == 4 then
			monster_skill_cast( 4, monster_handle, target_handle )
		elseif trigger_index == 5 then
			monster_skill_cast( 5, monster_handle, target_handle )
		end
	elseif monster_id == 86006 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, monster_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		elseif trigger_index == 2 then
			monster_skill_cast( 2, monster_handle, target_handle )
		elseif trigger_index == 3 then
			monster_skill_cast( 3, monster_handle, target_handle )
		elseif trigger_index == 4 then
			monster_skill_cast( 4, monster_handle, target_handle )
		elseif trigger_index == 5 then
			monster_skill_cast( 5, monster_handle, target_handle )
		end
	elseif monster_id == 104007 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, monster_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		elseif trigger_index == 2 then
			monster_skill_cast( 2, monster_handle, target_handle )
		elseif trigger_index == 3 then
			monster_skill_cast( 3, monster_handle, target_handle )
		elseif trigger_index == 4 then
			monster_skill_cast( 4, monster_handle, target_handle )
		elseif trigger_index == 5 then
			monster_skill_cast( 5, monster_handle, target_handle )
		end
	elseif monster_id == 15011 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 15012 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 9060016 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, monster_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 2, monster_handle, target_handle )
		elseif trigger_index == 2 then
			monster_skill_cast( 1, monster_handle, target_handle )
		elseif trigger_index == 3 then
			respawn_near_monster( monster_handle, 2055001, 6 )
		end
	elseif monster_id == 9108022 or monster_id == 10108022 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, monster_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		elseif trigger_index == 2 then
			monster_skill_cast( 2, monster_handle, target_handle )
		elseif trigger_index == 3 then
			monster_skill_cast( 3, monster_handle, target_handle )
		end
	elseif monster_id == 9110015 or monster_id == 10110015 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, monster_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		elseif trigger_index == 2 then
			monster_skill_cast( 2, monster_handle, target_handle )
		elseif trigger_index == 3 then
			monster_skill_cast( 3, monster_handle, target_handle )
		end
	elseif monster_id == 9070012 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, monster_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		elseif trigger_index == 2 then
			monster_skill_cast( 2, monster_handle, target_handle )
		elseif trigger_index == 3 then
			respawn_near_monster( monster_handle, 2065001, 6 )
		elseif trigger_index == 4 then
			monster_skill_cast( 4, monster_handle, target_handle )
		elseif trigger_index == 5 then
			monster_skill_cast( 5, monster_handle, target_handle )
		end
	elseif monster_id == 9080014 or monster_id == 10080014 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, monster_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 2, monster_handle, target_handle )
		elseif trigger_index == 2 then
			monster_skill_cast( 1, monster_handle, target_handle )
		elseif trigger_index == 3 then
			monster_skill_cast( 3, monster_handle, target_handle )
		end
	elseif monster_id == 9090019 or monster_id == 10090019 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, monster_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 2, monster_handle, target_handle )
		elseif trigger_index == 2 then
			monster_skill_cast( 1, monster_handle, target_handle )
		elseif trigger_index == 3 then
			monster_skill_cast( 3, monster_handle, target_handle )
		end
	elseif monster_id == 9050013 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, monster_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		elseif trigger_index == 2 then
			monster_skill_cast( 2, monster_handle, target_handle )
		elseif trigger_index == 3 then
			monster_skill_cast( 3, monster_handle, target_handle )
		end
	elseif monster_id == 9040018 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, monster_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 2, monster_handle, target_handle )
		elseif trigger_index == 2 then
			monster_skill_cast( 3, monster_handle, target_handle )
		elseif trigger_index == 3 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 9158002 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, monster_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		elseif trigger_index == 2 then
			monster_skill_cast( 2, monster_handle, target_handle )
		elseif trigger_index == 3 then
			monster_skill_cast( 3, monster_handle, target_handle )
		end
	elseif monster_id == 9170001 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, monster_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		elseif trigger_index == 2 then
			monster_skill_cast( 2, monster_handle, target_handle )
		elseif trigger_index == 3 then
			monster_skill_cast( 3, monster_handle, target_handle )
		end
	elseif monster_id == 9190001 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, monster_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		elseif trigger_index == 2 then
			monster_skill_cast( 2, monster_handle, target_handle )
		elseif trigger_index == 3 then
			monster_skill_cast( 3, monster_handle, target_handle )
		end
	elseif monster_id == 9155008 or monster_id == 10155008 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
		elseif monster_id == 9300001 or monster_id == 9300002 or monster_id == 9300003 or monster_id == 9300004 or monster_id == 9300005 or monster_id == 9300006 or monster_id == 9300007 or monster_id == 9300008 or monster_id == 9300009 or monster_id == 9300010 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, monster_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 16 or monster_id == 108003 or monster_id == 112003 or monster_id == 112004 or monster_id == 113004 or monster_id == 119004 or monster_id == 122002 or monster_id == 123003 or monster_id == 128002 or monster_id == 131002 or monster_id == 102006 then
		if trigger_index == 0 then
			monster_skill_cast( 2, monster_handle, target_handle )
		end
	elseif monster_id == 17 or monster_id == 113003 or monster_id == 116004 or monster_id == 120001 or monster_id == 121001 or monster_id == 123002 or monster_id == 126002 or monster_id == 143001 then
		if trigger_index == 0 then
			monster_skill_cast( 2, monster_handle, target_handle )
		end
	elseif monster_id == 18 or monster_id == 115004 or monster_id == 120002 or monster_id == 121002 or monster_id == 123004 or monster_id == 125003 or monster_id == 125004 or monster_id == 129002 or monster_id == 145001 or monster_id == 103006 then
		if trigger_index == 0 then
			monster_skill_cast( 2, monster_handle, target_handle )
		end
	elseif monster_id == 19 or monster_id == 124001 or monster_id == 127003 or monster_id == 132002 or monster_id == 133003 or monster_id == 135001 or monster_id == 136002 or monster_id == 140001 then
		if trigger_index == 0 then
			monster_skill_cast( 2, monster_handle, target_handle )
		end
	elseif monster_id == 115005 or monster_id == 121003 or monster_id == 137002 or monster_id == 138001 or monster_id == 144002 or monster_id == 143004 then
		if trigger_index == 0 then
			monster_skill_cast( 2, monster_handle, target_handle )
		end
	elseif monster_id == 139001 or monster_id == 139002 or monster_id == 144001 or monster_id == 150001 then
		if trigger_index == 0 then
			monster_skill_cast( 2, monster_handle, target_handle )
		end
	elseif monster_id == 130004 or monster_id == 138002 or monster_id == 140002 or monster_id == 146002  or monster_id == 163004 then
		if trigger_index == 0 then
			monster_skill_cast( 2, monster_handle, monster_handle )
		end
	elseif monster_id == 141003 or monster_id == 145003 or monster_id == 150003 then
		if trigger_index == 0 then
			monster_skill_cast( 2, monster_handle, monster_handle )
		end
	elseif monster_id == 132003 or monster_id == 135002 or monster_id == 143002 then
		if trigger_index == 0 then
			monster_skill_cast( 2, monster_handle, monster_handle )
		end
	elseif monster_id == 141002 or monster_id == 142002 or monster_id == 146001 then
		if trigger_index == 0 then
			monster_skill_cast( 2, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 3, monster_handle, target_handle )
		end
	elseif monster_id == 141001 or monster_id == 142001 or monster_id == 147001 or monster_id == 148001 or monster_id == 142004 or monster_id == 144004 then
		if trigger_index == 0 then
			monster_skill_cast( 2, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 3, monster_handle, target_handle )
		end
	elseif monster_id == 145002 or monster_id == 147002 or monster_id == 145004 or monster_id == 149002 then
		if trigger_index == 0 then
			monster_skill_cast( 2, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 3, monster_handle, target_handle )
		end
	elseif monster_id == 150002 then
		if trigger_index == 0 then
			monster_skill_cast( 2, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 3, monster_handle, target_handle )
		end
	elseif monster_id == 5041 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 5043 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 5044 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 5046 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 5049 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 10041 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 10043 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 10044 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 10046 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 10049 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 15041 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 15043 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 15044 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 15046 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 15049 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 20041 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 20043 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 20044 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 20046 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 20049 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 25041 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 25043 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 25044 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 25046 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 25049 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 30041 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 30043 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 30044 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 30046 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 30049 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 35041 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 35043 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 35044 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 35046 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 35049 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 40041 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 40043 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 40044 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 40046 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 40049 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 45041 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 45043 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 45044 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 45046 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 45049 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 50041 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 50043 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 50044 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 50046 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 50049 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 55041 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 55043 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 55044 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 55046 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 55049 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 60041 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 60043 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 60044 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 60046 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 60049 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 65041 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 65043 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 65044 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 65046 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 65049 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 70041 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 70043 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 70044 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 70046 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 70049 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 75041 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 75043 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 75044 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 75046 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 75049 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 80041 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 80043 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 80044 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 80046 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 80049 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 85041 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 85043 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 85044 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 85046 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 85049 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 90041 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 90043 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 90044 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 90046 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 90049 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 95041 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 95043 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 95044 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 95046 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 95049 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 100041 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 100043 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 100044 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 100046 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 100049 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 105041 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 105043 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 105044 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 105046 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 105049 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 110041 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 110043 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 105044 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 105046 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 105049 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 110041 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 110043 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 110044 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 110046 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 110049 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 115041 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 115043 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 115044 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 115046 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 115049 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 120041 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 120043 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 120044 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 120046 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 120049 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 125041 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 125043 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 125044 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 125046 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 125049 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 105041 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 130043 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 130044 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 130046 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 130049 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 135041 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 135043 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 135044 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 135046 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 135049 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 140041 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 140043 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 140044 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 140046 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 140049 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 145041 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 145043 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 145044 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 145046 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 145049 then
		if trigger_index == 0 then
			add_state(5997,  9, 500, target_handle)
		elseif trigger_index == 1 then
			add_state(13005, 50, 12000, target_handle)
			add_state(5997, 11, 8640000, target_handle)
			set_auto_user( 1, target_handle )
		end
	elseif monster_id == 956 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		elseif trigger_index == 2 then
			monster_skill_cast( 2, monster_handle, target_handle )
		elseif trigger_index == 3 then
			monster_skill_cast( 3, monster_handle, target_handle )
		elseif trigger_index == 4 then
			monster_skill_cast( 4, monster_handle, target_handle )
		elseif trigger_index == 5 then
			monster_skill_cast( 5, monster_handle, target_handle )
		elseif trigger_index == 6 then
			monster_skill_cast( 6, monster_handle, target_handle )
		elseif trigger_index == 7 then
			monster_skill_cast( 7, monster_handle, target_handle )
		elseif trigger_index == 8 then
			monster_skill_cast( 8, monster_handle, target_handle )
		elseif trigger_index == 9 then
			monster_skill_cast( 9, monster_handle, target_handle )
		end
	elseif monster_id == 110202  or monster_id == 110203 or monster_id == 110204 or monster_id == 110205 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, monster_handle )
		end
	elseif monster_id == 120202  or monster_id == 120203 or monster_id == 120204 or monster_id == 120205 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, monster_handle )
		end
	elseif monster_id == 130202  or monster_id == 130203 or monster_id == 130204 or monster_id == 130205 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, monster_handle )
		end
	elseif monster_id == 140202  or monster_id == 140203 or monster_id == 140204 or monster_id == 140205 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, monster_handle )
		end
	elseif monster_id == 150202  or monster_id == 150203 or monster_id == 150204 or monster_id == 150205 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, monster_handle )
		end
	elseif monster_id == 160202  or monster_id == 160203 or monster_id == 160204 or monster_id == 160205 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, monster_handle )
		end
	elseif monster_id == 170202  or monster_id == 170203 or monster_id == 170204 or monster_id == 170205 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, monster_handle )
		end
	elseif monster_id == 110102  or monster_id == 120102  or monster_id == 130102 or monster_id == 140102 or monster_id == 150102 or monster_id == 160102  or monster_id == 170102 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, monster_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		elseif trigger_index == 2 then
			monster_skill_cast( 2, monster_handle, target_handle )
		elseif trigger_index == 3 then
			monster_skill_cast( 3, monster_handle, target_handle )
		elseif trigger_index == 4 then
			monster_skill_cast( 5, monster_handle, target_handle )
		elseif trigger_index == 5 then
			monster_skill_cast( 5, monster_handle, target_handle )
		elseif trigger_index == 6 then
			monster_skill_cast( 6, monster_handle, target_handle )
		elseif trigger_index == 7 then
			monster_skill_cast( 7, monster_handle, target_handle )
		elseif trigger_index == 8 then
			monster_skill_cast( 8, monster_handle, target_handle )
		elseif trigger_index == 9 then
			monster_skill_cast( 9, monster_handle, target_handle )
		elseif trigger_index == 10 then
			monster_skill_cast( 10, monster_handle, target_handle )
		elseif trigger_index == 11 then
			monster_skill_cast( 11, monster_handle, target_handle )
		end
	elseif monster_id == 120201  or monster_id == 130201 or monster_id == 140201 or monster_id == 150201 or monster_id == 160201  or monster_id == 170201 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 129004 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 7122001  or monster_id == 7127001 or monster_id == 7141001 or monster_id == 7142001 or monster_id == 7146001 or monster_id == 7147001 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		end
	elseif monster_id == 7112001 or monster_id == 7113001 or monster_id ==7117001 or monster_id == 7118001 or monster_id == 7121001   or monster_id == 7126001 or monster_id == 7132001 or monster_id == 7137001 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		end
	elseif monster_id == 7115001  or monster_id == 7135001 or monster_id == 7145001 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 7110001 or monster_id == 7120001 or monster_id == 7130001 or monster_id == 7140001 or monster_id == 7150001 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, monster_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, monster_handle )
		 elseif trigger_index == 2 then
			 monster_skill_cast( 2, monster_handle, target_handle )
		elseif trigger_index == 3 then
			monster_skill_cast( 3, monster_handle, target_handle )
		elseif trigger_index == 4 then
			monster_skill_cast( 4, monster_handle, target_handle )
		elseif trigger_index == 5 then
			monster_skill_cast( 5, monster_handle, monster_handle )
		end
	elseif monster_id == 170001 or monster_id == 160001 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, monster_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, monster_handle )
		 elseif trigger_index == 2 then
			 monster_skill_cast( 2, monster_handle, target_handle )
		elseif trigger_index == 3 then
			monster_skill_cast( 3, monster_handle, target_handle )
		elseif trigger_index == 4 then
			monster_skill_cast( 4, monster_handle, target_handle )
		elseif trigger_index == 5 then
			monster_skill_cast( 5, monster_handle, monster_handle )
		end
	elseif monster_id == 253 or monster_id == 254 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, monster_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, monster_handle )
		end
	elseif monster_id == 910 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, monster_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, monster_handle )
		elseif trigger_index == 3 then
			monster_skill_cast( 3, monster_handle, target_handle )
		elseif trigger_index == 4 then
			monster_skill_cast( 4, monster_handle, target_handle )
		elseif trigger_index == 5 then
			monster_skill_cast( 5, monster_handle, monster_handle )
		end
	elseif monster_id == 920 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, monster_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, monster_handle )
		elseif trigger_index == 3 then
			monster_skill_cast( 3, monster_handle, target_handle )
		elseif trigger_index == 4 then
			monster_skill_cast( 4, monster_handle, target_handle )
		elseif trigger_index == 5 then
			monster_skill_cast( 5, monster_handle, monster_handle )
		end
	elseif monster_id == 950 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, monster_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, monster_handle )
		elseif trigger_index == 3 then
			monster_skill_cast( 3, monster_handle, target_handle )
		elseif trigger_index == 4 then
			monster_skill_cast( 4, monster_handle, target_handle )
		elseif trigger_index == 5 then
			monster_skill_cast( 5, monster_handle, monster_handle )
		end
	elseif monster_id == 951 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 923 or monster_id == 924 or monster_id == 926 or monster_id == 927 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		end
	elseif monster_id == 925 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 94002 or monster_id == 9073007 or monster_id == 144010 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		end
	elseif monster_id == 9073006 or monster_id == 9074006 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		end
	elseif monster_id == 132006 or monster_id == 135005 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		end
	elseif monster_id == 130007 or monster_id == 10130007 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 140003 or monster_id == 10140003 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 140004 or monster_id == 10140004 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 140005 or monster_id == 10140005 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 140006 or monster_id == 10140006 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 145010 or monster_id == 10145010 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, monster_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		elseif trigger_index == 2 then
			monster_skill_cast( 2, monster_handle, target_handle )
		elseif trigger_index == 3 then
			monster_skill_cast( 3, monster_handle, target_handle )
		elseif trigger_index == 4 then
			monster_skill_cast( 5, monster_handle, target_handle )
		elseif trigger_index == 5 then
			monster_skill_cast( 5, monster_handle, target_handle )
		elseif trigger_index == 6 then
			monster_skill_cast( 6, monster_handle, target_handle )
		end
	elseif monster_id == 135009 or monster_id == 126008 or monster_id == 10126008  or monster_id == 10135009 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		elseif trigger_index == 2 then
			monster_skill_cast( 2, monster_handle, target_handle )
		elseif trigger_index == 3 then
			monster_skill_cast( 3, monster_handle, target_handle )
		elseif trigger_index == 4 then
			monster_skill_cast( 4, monster_handle, target_handle )
		elseif trigger_index == 5 then
			monster_skill_cast( 5, monster_handle, target_handle )
		end
	elseif monster_id == 144007 or monster_id == 10144007 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		elseif trigger_index == 2 then
			monster_skill_cast( 2, monster_handle, target_handle )
		elseif trigger_index == 3 then
			monster_skill_cast( 3, monster_handle, target_handle )
		elseif trigger_index == 4 then
			monster_skill_cast( 4, monster_handle, target_handle )
		elseif trigger_index == 5 then
			monster_skill_cast( 5, monster_handle, target_handle )
		end
	elseif monster_id == 144008 or monster_id == 10144008 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		elseif trigger_index == 2 then
			monster_skill_cast( 2, monster_handle, target_handle )
		elseif trigger_index == 3 then
			monster_skill_cast( 3, monster_handle, target_handle )
		elseif trigger_index == 4 then
			monster_skill_cast( 4, monster_handle, target_handle )
		elseif trigger_index == 5 then
			monster_skill_cast( 5, monster_handle, target_handle )
		end
	elseif monster_id == 144009 or monster_id == 10144009 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		elseif trigger_index == 2 then
			monster_skill_cast( 2, monster_handle, target_handle )
		elseif trigger_index == 3 then
			monster_skill_cast( 3, monster_handle, target_handle )
		elseif trigger_index == 4 then
			monster_skill_cast( 4, monster_handle, target_handle )
		elseif trigger_index == 5 then
			monster_skill_cast( 5, monster_handle, target_handle )
		end
	elseif monster_id == 125007 or monster_id == 127006 or monster_id == 128004 or monster_id == 130009 or monster_id == 133008 or monster_id == 137004 or monster_id == 144012 or monster_id == 10125007 or monster_id == 10127006 or monster_id == 10128004 or monster_id == 10130009 or monster_id == 10133008 or monster_id == 10137004 or monster_id == 10144012 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		end
	elseif monster_id == 125008 or monster_id == 131008 or monster_id == 133007 or monster_id == 134007 or monster_id == 136004 or monster_id == 138003 or monster_id == 139006 or monster_id == 144011 or monster_id == 10125008 or monster_id == 10131008 or monster_id == 10133007 or monster_id == 10134007 or monster_id == 10136004 or monster_id == 10138003 or monster_id == 10139006 or monster_id == 10144011 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 126004 then
		if trigger_index == 1 then
			monster_skill_cast( 2, monster_handle, monster_handle )
		end
	elseif monster_id == 10091001 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 10092002 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 10093001 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 10093003 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 10094003 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 10094004 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 10095004 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 10095005 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 10096004 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 10098003 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 10098008 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, monster_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 10099005 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, monster_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 10100004 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, monster_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 10100013 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, monster_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 10100019 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, monster_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 10100020 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 10101004 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, monster_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 10101010 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, monster_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 10101013 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 10101019 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 10102005 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, monster_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 10102008 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 10103003 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, monster_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 10103008 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 10103011 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, monster_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 10104002 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, monster_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 10104009 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 10104022 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, monster_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 10105005 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, monster_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 10105014 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, monster_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id ==  10105019 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 10105026 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 10106006 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, monster_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 10106009 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 10106010 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 10106012 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, monster_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 10106027 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, monster_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 10107010 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 10107013 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, monster_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 10107021 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 10107022 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 10107023 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 10107026 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 10108004 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 10108014 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 10109004 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 10109012 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 10109022 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 10109023 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 10110002 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 10110010 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 10110017 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 10111003 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 10112001 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 10113003 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 131001 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 131003 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 131004 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 132001 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 132004 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 132005 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 133001 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 133002 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		end
	elseif monster_id == 120 or monster_id == 200001 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, monster_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, monster_handle )
		end
	elseif monster_id == 22000013 or monster_id == 22000026 or monster_id == 22000039 or monster_id == 22000052 then
		if trigger_index == 0 then
			monster_skill_cast( 2, monster_handle, monster_handle )
		end
	elseif monster_id == 22000081 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		elseif trigger_index == 2 then
			monster_skill_cast( 2, monster_handle, monster_handle )
		elseif trigger_index == 3 then
			monster_skill_cast( 3, monster_handle, monster_handle )
		elseif trigger_index == 4 then
			monster_skill_cast( 4, monster_handle, target_handle )
		elseif trigger_index == 5 then
			monster_skill_cast( 5, monster_handle, target_handle )
		end
	elseif monster_id == 22000082 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		elseif trigger_index == 2 then
			monster_skill_cast( 2, monster_handle, target_handle )
		elseif trigger_index == 3 then
			monster_skill_cast( 3, monster_handle, monster_handle )
		elseif trigger_index == 4 then
			monster_skill_cast( 4, monster_handle, monster_handle )
		end
	elseif monster_id == 22000083 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		elseif trigger_index == 2 then
			monster_skill_cast( 2, monster_handle, target_handle )
		elseif trigger_index == 3 then
			monster_skill_cast( 3, monster_handle, target_handle )
		elseif trigger_index == 4 then
			monster_skill_cast( 4, monster_handle, target_handle )
		elseif trigger_index == 5 then
			monster_skill_cast( 5, monster_handle, target_handle )
		end
	elseif monster_id == 22000084 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		elseif trigger_index == 2 then
			monster_skill_cast( 2, monster_handle, target_handle )
		elseif trigger_index == 3 then
			monster_skill_cast( 3, monster_handle, monster_handle )
		elseif trigger_index == 4 then
			monster_skill_cast( 4, monster_handle, target_handle )
		elseif trigger_index == 5 then
			monster_skill_cast( 5, monster_handle, target_handle )
		end
	elseif monster_id == 22000085 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		elseif trigger_index == 2 then
			monster_skill_cast( 2, monster_handle, target_handle )
		elseif trigger_index == 3 then
			monster_skill_cast( 3, monster_handle, monster_handle )
		elseif trigger_index == 4 then
			monster_skill_cast( 4, monster_handle, target_handle )
		elseif trigger_index == 5 then
			monster_skill_cast( 5, monster_handle, target_handle )
		end
	elseif monster_id == 22000086 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		elseif trigger_index == 2 then
			monster_skill_cast( 2, monster_handle, monster_handle )
		elseif trigger_index == 3 then
			monster_skill_cast( 3, monster_handle, monster_handle )
		end
	elseif monster_id == 22000087 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		elseif trigger_index == 2 then
			monster_skill_cast( 2, monster_handle, monster_handle )
		elseif trigger_index == 3 then
			monster_skill_cast( 3, monster_handle, monster_handle )
		elseif trigger_index == 4 then
			monster_skill_cast( 4, monster_handle, target_handle )
		end
	elseif monster_id == 22000088 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		elseif trigger_index == 2 then
			monster_skill_cast( 2, monster_handle, target_handle )
		elseif trigger_index == 3 then
			monster_skill_cast( 3, monster_handle, monster_handle )
		elseif trigger_index == 4 then
			monster_skill_cast( 4, monster_handle, monster_handle )
		end
	elseif monster_id == 22000089 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		elseif trigger_index == 2 then
			monster_skill_cast( 2, monster_handle, target_handle )
		elseif trigger_index == 3 then
			monster_skill_cast( 3, monster_handle, target_handle )
		elseif trigger_index == 4 then
			monster_skill_cast( 4, monster_handle, target_handle )
		end
	elseif monster_id == 22000090 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		elseif trigger_index == 2 then
			monster_skill_cast( 2, monster_handle, target_handle )
		elseif trigger_index == 3 then
			monster_skill_cast( 3, monster_handle, monster_handle )
		elseif trigger_index == 4 then
			monster_skill_cast( 4, monster_handle, target_handle )
		end
	elseif monster_id == 22000091 then
		if trigger_index == 0 then
			monster_skill_cast( 0, monster_handle, target_handle )
		elseif trigger_index == 1 then
			monster_skill_cast( 1, monster_handle, target_handle )
		elseif trigger_index == 2 then
			monster_skill_cast( 2, monster_handle, target_handle )
		elseif trigger_index == 3 then
			monster_skill_cast( 3, monster_handle, monster_handle )
		elseif trigger_index == 4 then
			monster_skill_cast( 4, monster_handle, target_handle )
		end
	end
end
