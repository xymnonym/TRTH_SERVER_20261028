-- Rank updates: remove login-triggered refresh; refresh all rank categories daily.
USE tr_game_db;
DELIMITER ;;
DROP PROCEDURE IF EXISTS usp_login;;
CREATE DEFINER=`root`@`localhost` PROCEDURE `usp_login`(IN `servernum` INT, IN `userid` VARCHAR(50), IN `authnum` BIGINT, IN `loginkey` INT, IN `regpcroom` INT, IN `ip` VARCHAR(20))
    NO SQL
BEGIN
	DECLARE usernum INT;
	DECLARE userexp BIGINT;
	DECLARE ladderpoint INT;
	DECLARE nowservernum INT;
	DECLARE gamemoney BIGINT;
	DECLARE attribute INT;
	DECLARE usernickname VARCHAR(50);
	DECLARE authusernum INT;
	DECLARE Livetime DATETIME;
	DECLARE playingTime INT;
	DECLARE lastLoginTime, lastLogoutTime DATETIME;
	DECLARE likeable, savedChannel, gameOption INT;
	DECLARE guildNum INT;
	DECLARE shuMP INT;
    DECLARE existsUserInfo BOOLEAN;
    DECLARE Newloginkey INT;
	DECLARE TopRank INT;
	DECLARE currentShuCharacterItemID BIGINT;
	DECLARE partyType SMALLINT;

	DECLARE couplenum INT;
	DECLARE matename VARCHAR(50);
	DECLARE createtime DATETIME;
	DECLARE coupleRingNum INT;
	DECLARE condDays INT;
	DECLARE maxRingDays INT;
	DECLARE coupleType INT;
	DECLARE marriedDateTime DATETIME;
	DECLARE ringChangedTime DATETIME;
	DECLARE accumulateExp INT;
	DECLARE couplePoint INT;
	DECLARE coupleLevel SMALLINT;

	DECLARE mileagePoint INT DEFAULT 0;
	DECLARE vipLevel INT DEFAULT 1;

	main_proc:BEGIN
		ipcheck:BEGIN
			DECLARE InvalidIP INT;
			SELECT fdNum INTO InvalidIP FROM tblInvalidLoginIP
			WHERE fdStartIP <= ip AND fdEndIP >= ip AND fdUse = 1 LIMIT 1;

			IF InvalidIP > 0 THEN
				SIGNAL SQLSTATE '45000'
				SET MESSAGE_TEXT = 'invalid country';
			END IF;
		END ipcheck;

		SET usernickname = NULL;

		SELECT t1.fdAuthUserNum, t1.fdUserNum, t1.fdNickname, t3.fdServerNum,
			t2.fdExp, t2.fdLadderPoint, t2.fdGameMoney, t1.fdAttribute,
			t3.fdPlayingTime, t3.fdLastLoginTime, t3.fdLastLogoutTime, t2.fdLikeable,
			t1.fdChannelNum, t1.fdOption
		INTO authusernum, usernum, usernickname, nowservernum,
			userexp, ladderpoint, gamemoney, attribute,
			playingTime, lastLoginTime, lastLogoutTime, likeable,
			savedChannel, gameOption
		FROM userinfo t1
		JOIN userinfogame t2 ON t1.fdUserNum = t2.fdUserNum
		JOIN userinfologin t3 ON t1.fdUserNum = t3.fdUserNum
		WHERE t1.fdUID = userid;

		SET existsUserInfo = ROW_COUNT();
		SET Newloginkey = ROUND(1 + 1000000000 * RAND((MONTH(NOW()) * 100000) + (SECOND(NOW()) * 1000) + (MICROSECOND(CURTIME(3))/1000)));

		blacklistcheck:BEGIN
			DECLARE blacklistdatetime DATETIME;
			DECLARE blockReasonNum INT DEFAULT 0;

			SELECT fdBlockEndDateTime, IFNULL(fdBlockReasonNum, 0) INTO blacklistdatetime, blockReasonNum
			FROM tblBlacklist WHERE fdUserNum = usernum;

			IF ROW_COUNT() <> 0 THEN
				IF blacklistdatetime IS NULL THEN
					SELECT usernum AS 'usernum', 1 AS 'blackListCheck', blockReasonNum AS 'blockReason';
					LEAVE main_proc;
				END IF;

				IF blacklistdatetime < NOW() THEN
					DELETE FROM tblBlackList WHERE fdUserNum = usernum;
				ELSE
					SELECT usernum AS 'usernum', 1 AS 'blackListCheck', blockReasonNum AS 'blockReason';
					LEAVE main_proc;
				END IF;
			END IF;
		END blacklistcheck;

		IF existsUserInfo = 0 THEN
			SET userexp = 0;
			SET ladderpoint = 10000;
			SET nowservernum = 0;
			SET gamemoney = 1000000;
			SET attribute = 0;
			SET authusernum = authnum;
			SET LiveTime = NOW();
			SET @Initial_game_money = 0;

			SELECT CAST(fdValue AS SIGNED) INTO @Initial_game_money
			FROM tblServerSettingInfo WHERE fdKey = 'InitialGameMoney';

			IF @Initial_game_money <> 0 THEN
				SET gamemoney = @Initial_game_money;
			END	IF;

			INSERT INTO UserInfo (fdAuthUserNum, fdChannelNum, fdUID) VALUES (authnum, -1, userid);

			SELECT @@IDENTITY INTO usernum;

			INSERT INTO UserInfoGame (fdUserNum, fdGameMoney) VALUES (usernum, gamemoney);

			INSERT INTO UserInfoLogin (fdUserNum) VALUES (usernum);

		END IF;

		SELECT fdMP INTO shuMP FROM UserShuInfo WHERE fdUserNum = usernum;
		IF ROW_COUNT() = 0 THEN
			INSERT UserShuInfo VALUES (usernum, 5, 0, NOW());
			SET shuMP = 5;
		END IF;

		IF nowservernum = 0 THEN
			UPDATE userinfologin SET fdLoginKey = Newloginkey, fdServerNum = servernum, fdLastLoginTime = NOW(), fdLoginCount = fdLoginCount + 1 WHERE fdUserNum = usernum;
			INSERT INTO user_recent_activity (UserNum, LastLogin) VALUES (usernum, NOW());

			SELECT fdCoupleNum, fdUserNum INTO couplenum, @mateUserNum
			FROM tblCoupleUserInfo WHERE fdUserNum <> usernum
			AND fdCoupleNum IN (SELECT fdCoupleNum FROM tblCoupleUserInfo WHERE fdUserNum = usernum);

			SELECT fdNickname INTO matename FROM UserInfo WHERE fdUserNum = @mateUserNum;

			SELECT fdCreateDateTime, fdCoupleRingNum, fdMaxRequireDays,
				fdCoupleType, fdMarriedDateTime, fdRingChangedTime,
				fdAccumulateExp, fdCouplePoin
			INTO createtime, coupleRingNum, maxRingDays,
				coupleType, marriedDateTime, ringChangedTime,
				accumulateExp, couplePoin
			FROM tblCoupleInfo WHERE fdCoupleNum = couplenum;

			DROP TEMPORARY TABLE IF EXISTS tblCoupleRingInfo;
			CREATE TEMPORARY TABLE tblCoupleRingInfo ENGINE = MEMORY
				SELECT fdAttr, fdValue FROM tblAvatarItemDescAttr WHERE fdItemNum = coupleRingNum;
			SELECT fdValue INTO condDays FROM tblCoupleRingInfo WHERE fdAttr = 54;
			SELECT fdValue INTO coupleLevel FROM tblCoupleRingInfo WHERE fdAttr = 156;
			DROP TEMPORARY TABLE IF EXISTS tblCoupleRingInfo;

		ELSEIF nowservernum <> 0 THEN
			UPDATE userinfologin SET fdServerNum = 0 WHERE fdUserNum = usernum;
		END IF;

		IF usernickname IS NULL THEN
			SET usernickname = '_firstLogin_';
		END IF;

		IF DATEDIFF(NOW(), lastLoginTime) <> 0 THEN
			CALL usp_guild_increaseAttendance(usernum);
		END IF;

		attendance:BEGIN
			DECLARE return_date SMALLINT;
			DECLARE AttendanceKey INT;

			SET return_date = TIMESTAMPDIFF(DAY, lastLogoutTime, NOW());

			IF usernickname = '_firstLogin_' THEN
				SELECT fdAttendanceKey INTO AttendanceKey FROM EssenAttendanceInfo WHERE fdEnable > 0 AND fdAttendanceType = 0 LIMIT 1;
				IF AttendanceKey IS NOT NULL THEN
					IF NOT EXISTS(SELECT * FROM UserAttendanceAllowed WHERE fdAttendanceKey = AttendanceKey AND fdUsernum = usernum) THEN
						CALL usp_attendanceAllow(AttendanceKey, usernum, 0);











					END IF;
				END IF;
			END IF;

			IF return_date >= 15 THEN
				SELECT fdAttendanceKey INTO AttendanceKey FROM EssenAttendanceInfo WHERE fdEnable > 0 AND fdAttendanceType = 1 LIMIT 1;
				IF AttendanceKey IS NOT NULL THEN
					IF NOT EXISTS(SELECT * FROM UserAttendanceAllowed WHERE fdAttendanceKey = AttendanceKey AND fdUsernum = usernum AND NOW() BETWEEN fdStart AND fdEnd) THEN
						CALL usp_attendanceAllow(AttendanceKey, usernum, 0);
				END IF;
				END IF;
			END IF;

		END;


		SET @guildPoint = 0;
		SELECT fdGuildNum, fdContributionPoint INTO guildNum, @guildPoint FROM UserGuild WHERE fdUserNum = usernum;

		SELECT fdPoint INTO mileagePoint FROM UserInfoPoint WHERE fdUserNum = usernum AND fdRewardCondition = 1201;
		SELECT fdPoint + mileagePoint INTO mileagePoint FROM UserInfoPointMonthReset WHERE fdUserNum = usernum AND fdRewardCondition = 1202 AND fdMonth = DATE_FORMAT(NOW(), '%Y-%m-01');

		CALL usp_renewalShop_getUserVipLevel2(usernum, vipLevel);

		SET @eventType = 0;
		SELECT CAST(fdValue AS SIGNED) INTO @eventType FROM tblServerSettingInfo WHERE fdKey = 'useCompetitionEvent';

		SELECT fdPartyType INTO partyType FROM UserCompetitionEventPartyInfo
		WHERE fdUserNum = usernum AND fdPartyType IN (SELECT fdPartyType FROM EssenCompetitionEventInfo WHERE fdEventType = @eventType);

		CALL usp_shu_checkSatiety(userNum, 0);

		SELECT fdCharacterItemID INTO currentShuCharacterItemID FROM UserShuCharacterInfo WHERE fdUserNum = usernum AND fdState = 1;
		UPDATE UserShuCharacterInfo SET fdEquipStartTime = NOW() WHERE fdUserNum = usernum AND fdState = 1;

		IF currentShuCharacterItemID IS NOT NULL AND
			(SELECT fdValue FROM UserShuStatus WHERE fdUserNum = usernum AND fdCharacterItemID = currentShuCharacterItemID AND fdStatusType = 0) <= 0 THEN
			UPDATE UserShuCharacterInfo SET fdEquipStartTime = NULL, fdState = 0 WHERE fdUserNum = userNum AND fdCharacterItemID = currentShuCharacterItemID;
			SET currentShuCharacterItemID = -1;
		END IF;

		SELECT fdRank INTO TopRank FROM tblRankTop WHERE fdUserNum = usernum;

		DELETE FROM UserCommunityMemo WHERE fdUserNum = usernum AND fdMemberNum NOT IN
		(SELECT fdFriendUserNum FROM tblCM_FriendList WHERE fdUserNum = usernum
		  UNION ALL
		SELECT fdUserNum FROM UserGuild WHERE fdGuildNum = guildNum);

		UPDATE UserTalesKnightsGroupInfo SET fdStartTime = NOW()
		WHERE fdUserNum = usernum AND fdStageNum <> 0 AND fdRewardTime > 0;

		CALL usp_GiveTalesKnightNewbiePack(usernum);
		CALL usp_TalesKnightRewardToGift(usernum);

		SELECT usernum as userindex,
			IFNULL(usernickname, '') as nickName,
			userid as userID,
			userexp as userexp,
			ladderpoint as ladderpoint,
			nowservernum as lastservernum,
			gamemoney as gamemoney,
			attribute as attribute,
			authusernum as authusernum,
			IFNULL(couplenum, -1) AS couplenum,
			IFNULL(matename, '') AS matename,
			IFNULL(createtime, 0) AS createtime,
			IFNULL(coupleRingNum, -1) AS coupleRingNum,
			IFNULL(condDays, -1) AS condDays,
			IFNULL(coupleLevel, -1) AS coupleLevel,
			IFNULL(maxRingDays, -1) AS maxCoupleRingDays,
			IFNULL(coupleType, 0) AS coupleType,
			IFNULL(marriedDateTime, 0) AS marriedDateTime,
			IFNULL(ringChangedTime, 0) AS ringChangedTime,
			IFNULL(accumulateExp, 0) AS accumulateExp,
			IFNULL(couplePoint, 0) AS couplePoint,
			IFNULL(playingTime,0) as playingTime,
			lastLoginTime as lastLoginTime,
			IFNULL(guildNum, -1) AS guildNum,
			IFNULL(likeable,0) as likeable,
			IFNULL(partyType, 0) AS partyType,
			0 AS blackListCheck,
			IFNULL(gameOption, 0) as gameOption,
			@guildPoint AS guildPoint,
			IFNULL(shuMP, 0) as shuMP,
			IFNULL(currentShuCharacterItemID, -1) as currentShuCharacterItemID,
			IFNULL(TopRank, 0) as TopRank,
			Newloginkey as loginkey,
			IFNULL(mileagePoint, 0) AS mileagePoint,
			IFNULL(vipLevel, 1) AS vipLevel;
	END main_proc;
END;;
ALTER EVENT EssenMakeRankDaily DO BEGIN
  CALL usp_makerank(0);
  CALL usp_makerank(1);
  CALL usp_makerank(2);
  CALL usp_makerank(3);
END;;
DELIMITER ;
SET GLOBAL event_scheduler = ON;
